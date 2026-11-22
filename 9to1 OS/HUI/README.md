# HUI local provider contracts

The HUI registry describes the local llama.cpp provider through `llamacpp-provider.json`. Consumers use the provider identity, model key, status and streaming routes from that contract. The broker owns inference and model lifecycle. This directory contains the HUI platform hosts and their shared contracts. [Provider declaration](llamacpp-provider.json)

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
`../../9to1 Workspace/Home/Source/Dulche/llamacpp/provider-contract.json`; this file only defines the HUI
adapter surface.
