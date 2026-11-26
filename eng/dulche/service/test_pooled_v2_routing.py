"""Verify pooled reasoning normalization at the HTTP transport boundary, without inference."""
import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

import engines
from engine_profiles import CPU, INKLING, INKLING_POOL, PRIMARY, V100


class PooledReasoningRoutingTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.manager = engines.EngineManager(Path(temporary.name))

    def route(self, model_id, payload, *, stream=False):
        original = copy.deepcopy(payload)
        response = mock.Mock(status=200)
        connection = mock.Mock()
        connection.getresponse.return_value = response
        selected = {"id": model_id}
        with mock.patch.object(self.manager, "_spec", return_value=selected) as spec, \
             mock.patch.object(self.manager, "_observe", return_value=(
                 {"state": "loaded"}, (12345, "fixture-key", "owned-fixture", 123), {})) as observe, \
             mock.patch.object(engines.http.client, "HTTPConnection", return_value=connection) as factory:
            with self.manager.generate(model_id, payload, stream=stream) as received:
                self.assertIs(received, response)
        spec.assert_called_once_with(model_id)
        observe.assert_called_once_with(selected)
        factory.assert_called_once_with("127.0.0.1", 12345, timeout=1200)
        connection.connect.assert_called_once_with()
        connection.close.assert_called_once_with()
        response.close.assert_called_once_with()
        method, path, body, headers = connection.request.call_args.args
        self.assertEqual((method, path), ("POST", "/v1/chat/completions"))
        self.assertEqual(headers["Authorization"], "Bearer fixture-key")
        self.assertEqual(payload, original, "Generation must not mutate its caller's payload")
        data = json.loads(body)
        self.assertEqual(data["model"], model_id)
        self.assertIs(data["stream"], bool(stream))
        return data

    @staticmethod
    def payload(**settings):
        return {"messages": [{"role": "user", "content": "Write a small function."}],
                "model": "caller-alias-is-not-the-route", "stream": "caller-value",
                "max_tokens": 128, "temperature": 0, "custom_metadata": {"retained": True},
                **settings}

    def test_explicit_off_starts_answer_content_and_preserves_flags_and_route(self):
        choices = ({"reasoning_effort": "none"},
                   {"chat_template_kwargs": {"enable_thinking": False, "other": "retained"}},
                   {"reasoning_effort": "none", "chat_template_kwargs": {"enable_thinking": True}},
                   {"reasoning_effort": "high", "chat_template_kwargs": {"enable_thinking": False}})
        for settings in choices:
            for stream in (False, True):
                with self.subTest(settings=settings, stream=stream):
                    payload = self.payload(**settings)
                    data = self.route(INKLING_POOL, payload, stream=stream)
                    self.assertEqual(data, {**payload, "model": INKLING_POOL, "stream": stream,
                                            "reasoning_budget_tokens": 0,
                                            "messages": [*payload["messages"], {"role": "assistant", "content": [{"type": "text", "text": ""}]}],
                                            "continue_final_message": "content", "add_generation_prompt": False,
                                            "stop": ["<|end_message|>"]})

    def test_explicit_stop_sequences_are_preserved(self):
        for stop in (["owner delimiter"], "owner delimiter", []):
            with self.subTest(stop=stop):
                result = self.route(INKLING_POOL, self.payload(reasoning_effort="none", stop=stop))
                self.assertEqual(stop, result["stop"])

    def test_explicit_budget_is_preserved_even_when_thinking_is_off(self):
        for settings in ({"reasoning_effort": "none"}, {"chat_template_kwargs": {"enable_thinking": False}}):
            for budget in (0, 128, -1, None, False, "0"):
                with self.subTest(settings=settings, budget=budget):
                    payload = self.payload(**settings, reasoning_budget_tokens=budget)
                    self.assertEqual(self.route(INKLING_POOL, payload),
                                     {**payload, "model": INKLING_POOL, "stream": False})

    def test_explicit_zero_budget_is_not_reinterpreted_as_a_new_prefill(self):
        for field in ("reasoning_budget_tokens", "thinking_budget_tokens"):
            with self.subTest(field=field):
                payload = self.payload(reasoning_effort="none", **{field: 0})
                self.assertEqual(self.route(INKLING_POOL, payload),
                                 {**payload, "model": INKLING_POOL, "stream": False})

    def test_explicit_alias_budget_and_canonical_precedence_are_preserved(self):
        for settings in ({"thinking_budget_tokens": 128},
                         {"thinking_budget_tokens": None},
                         {"reasoning_budget_tokens": 128, "thinking_budget_tokens": 0}):
            with self.subTest(settings=settings):
                payload = self.payload(reasoning_effort="none", **settings)
                self.assertEqual(self.route(INKLING_POOL, payload),
                                 {**payload, "model": INKLING_POOL, "stream": False})

    def test_explicit_continuation_and_generation_controls_are_preserved(self):
        for settings in ({"continue_final_message": False},
                         {"continue_final_message": "reasoning_content"},
                         {"continue_final_message": "content"},
                         {"add_generation_prompt": True},
                         {"add_generation_prompt": False}):
            with self.subTest(settings=settings):
                payload = self.payload(reasoning_effort="none", **settings)
                self.assertEqual(self.route(INKLING_POOL, payload),
                                 {**payload, "model": INKLING_POOL, "stream": False,
                                  "reasoning_budget_tokens": 0})

    def test_active_tools_and_existing_assistant_prefill_are_preserved(self):
        function = {"type": "function", "function": {"name": "lookup", "parameters": {"type": "object"}}}
        choices = ({"tools": [function]}, {"tools": [function], "tool_choice": "required"},
                   {"tools": [function], "tool_choice": {"type": "function", "function": {"name": "lookup"}}},
                   {"messages": [{"role": "user", "content": "hello"}, {"role": "assistant", "content": "A prefill"}]},
                   {"messages": []}, {"messages": "malformed-is-not-rewritten"})
        for settings in choices:
            with self.subTest(settings=settings):
                payload = self.payload(reasoning_effort="none", **settings)
                self.assertEqual(self.route(INKLING_POOL, payload),
                                 {**payload, "model": INKLING_POOL, "stream": False,
                                  "reasoning_budget_tokens": 0})

    def test_structured_output_and_typed_input_are_not_rewritten(self):
        choices = ({"response_format": {"type": "json_object"}},
                   {"response_format": {"type": "text"}},
                   {"grammar": 'root ::= "ok"'}, {"json_schema": {"type": "object"}},
                   {"messages": [{"role": "user", "content": [{"type": "text", "text": "hello"}]}]})
        for settings in choices:
            with self.subTest(settings=settings):
                payload = self.payload(reasoning_effort="none", **settings)
                self.assertEqual(self.route(INKLING_POOL, payload),
                                 {**payload, "model": INKLING_POOL, "stream": False,
                                  "reasoning_budget_tokens": 0})

    def test_prefix_is_idempotent_and_does_not_disable_explicitly_unused_tools(self):
        payload = self.payload(reasoning_effort="none", tools=[{"type": "function"}], tool_choice="none")
        once = self.route(INKLING_POOL, payload)
        self.assertEqual(once["continue_final_message"], "content")
        self.assertEqual(self.route(INKLING_POOL, once), once)

    def test_counting_uses_the_generation_prefix_and_counts_its_two_tokens(self):
        for options in ({"reasoning_effort": "none"},
                        {"chat_template_kwargs": {"enable_thinking": False}},
                        {"reasoning_effort": "high"}):
            with self.subTest(options=options):
                payload = self.payload(**options)
                original = copy.deepcopy(payload)
                generated = self.route(INKLING_POOL, payload)
                calls = []
                replies = []

                def request(method, route, raw, headers):
                    self.assertEqual(method, "POST")
                    self.assertEqual(headers["Authorization"], "Bearer fixture-key")
                    body = json.loads(raw)
                    calls.append((route, body))
                    if route == "/apply-template":
                        is_prefix = (
                            body.get("continue_final_message") == "content"
                            and body.get("add_generation_prompt") is False
                            and body["messages"][-1] == {"role": "assistant", "content": [{"type": "text", "text": ""}]}
                        )
                        result = {"prompt": "fixture prompt" + ("<|message_model|><|content_text|>" if is_prefix else "")}
                    else:
                        self.assertEqual(route, "/tokenize")
                        self.assertIs(body["add_special"], True)
                        self.assertIs(body["parse_special"], True)
                        # The pinned backend treats these as two special tokens, not ordinary text.
                        result = {"tokens": [1, 2, 3] + ([200001, 200002] if body["content"].endswith("<|message_model|><|content_text|>") else [])}
                    replies.append(json.dumps(result).encode())

                response = mock.Mock(status=200)
                response.read.side_effect = lambda *_: replies.pop(0)
                connection = mock.Mock()
                connection.request.side_effect = request
                connection.getresponse.return_value = response
                with mock.patch.object(self.manager, "_spec", return_value={"id": INKLING_POOL}), \
                     mock.patch.object(self.manager, "_observe", return_value=(
                         {"state": "loaded"}, (12345, "fixture-key", "owned-fixture", 123), {})), \
                     mock.patch.object(engines.http.client, "HTTPConnection", return_value=connection):
                    count = self.manager.count_prompt(INKLING_POOL, payload["messages"], options)

                expected = {key: value for key, value in generated.items()
                            if key in {"messages", "reasoning_effort", "chat_template_kwargs",
                                       "reasoning_budget_tokens", "continue_final_message", "add_generation_prompt", "stop"}}
                self.assertEqual(calls[0], ("/apply-template", expected))
                self.assertEqual(count["input_tokens"], 5 if "continue_final_message" in generated else 3)
                self.assertEqual(count["margin_tokens"], 0)
                self.assertEqual(payload, original)

    def test_pool_request_without_explicit_off_is_unchanged(self):
        choices = ({}, {"reasoning_effort": "low"}, {"reasoning_effort": "high"},
                   {"chat_template_kwargs": {"enable_thinking": True}},
                   {"chat_template_kwargs": {"enable_thinking": 0}},
                   {"chat_template_kwargs": {"enable_thinking": "false"}},
                   {"chat_template_kwargs": {}}, {"chat_template_kwargs": None},
                   {"chat_template_kwargs": "malformed-is-not-explicit-false"})
        for settings in choices:
            with self.subTest(settings=settings):
                payload = self.payload(**settings)
                self.assertEqual(self.route(INKLING_POOL, payload),
                                 {**payload, "model": INKLING_POOL, "stream": False})

    def test_other_models_keep_their_existing_reasoning_payload(self):
        for model in (INKLING, PRIMARY, V100, CPU, "local-fixture-model"):
            for budget in ({}, {"reasoning_budget_tokens": 128}):
                with self.subTest(model=model, budget=budget):
                    payload = self.payload(reasoning_effort="none",
                                           chat_template_kwargs={"enable_thinking": False}, **budget)
                    self.assertEqual(self.route(model, payload, stream=True),
                                     {**payload, "model": model, "stream": True})


if __name__ == "__main__":
    unittest.main()
