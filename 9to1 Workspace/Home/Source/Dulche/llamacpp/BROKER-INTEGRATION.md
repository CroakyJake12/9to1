# Model-free broker integration evidence

`test_broker_integration.py` exercises the Haven broker across real Unix sockets and a real supervised subprocess while using a deliberately fake llama worker. It exists to prove Haven's process/transport mechanics without downloading or executing a model.

The fake worker implements only the llama-server endpoints needed for the test (`/health` and streaming `/v1/chat/completions`). The test then exercises:

1. broker health before a worker exists;
2. GGUF admission of a generated tiny test fixture (header/hash/path only — not a model);
3. subprocess spawn and worker health handshake;
4. private worker-socket mode;
5. model list/load state and `llamacpp:` provider key;
6. streaming proxy delivery and `[DONE]` propagation;
7. active cancellation of a worker stream blocked after response headers;
8. unload, worker termination and socket cleanup.

Passing this test means the **Haven broker integration path** is repository-tested. It does not mean llama.cpp itself generated tokens, a real GGUF loaded, model compatibility is proven, performance was benchmarked, systemd supervision ran, the Debian package was installed, or the approved VM was modified.
