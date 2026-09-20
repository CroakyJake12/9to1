# Haven UI provider contract

`llamacpp-provider.json` is the HUI-facing registry entry for the local
llama.cpp provider. It describes discovery, status, model-key resolution, and
streaming execution without bundling a model or requiring a network service.

The registry entry is an adapter contract, not a second inference
implementation:

- probe `GET /v1/provider` and accept only the declared `llamacpp` identity;
- use `GET /health` and `GET /v1/models` for provider/model status;
- resolve `llamacpp:<model-id>` to this provider;
- keep unqualified model names mapped to the existing Ollama provider;
- send streaming requests to `POST /v1/chat/completions` with a safe,
  unique `request_id`, and use the cancel endpoint for cancellation.

Discovery and provider status are repository-contract/model-free evidence until
the HUI runs against a live `$XDG_RUNTIME_DIR/haven/inference.sock`. Model
execution remains unproven until an approved local GGUF is loaded by the
runtime.

The authoritative runtime details remain in
`runtime/llamacpp/provider-contract.json`; this file only defines the HUI
adapter surface.
