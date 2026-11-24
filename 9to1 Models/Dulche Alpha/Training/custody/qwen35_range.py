"""Narrow Qwen3.5 5.20.1 text-only, cache-free decoder-range adapter."""

from .artifacts import CustodyError

_TRANSFORMERS = "5.20.1"
_LAYER_TYPES = {"full_attention", "linear_attention"}


class Qwen35TextRange:
    """Use original decoder indices and native helpers; never register/load weights.

    Construct this view before discarding the enclosing text model on a rank.
    It retains only its original decoder modules, rotary, and either embedding
    (early) or final norm (tail). The launcher owns placement and surrounding
    autocast, loss, and backward transport.
    """

    def __init__(self, text, *, split: int, rank: int):
        import torch
        import transformers
        from transformers.masking_utils import create_causal_mask, create_recurrent_attention_mask
        from transformers.models.qwen3_5.modeling_qwen3_5 import Qwen3_5TextModel

        if transformers.__version__ != _TRANSFORMERS or type(text) is not Qwen3_5TextModel:
            raise CustodyError("Qwen3.5 text range requires the reviewed Transformers 5.20.1 ABI.")
        config = text.config
        count = config.num_hidden_layers
        if (type(split) is not int or not 0 < split < count or type(rank) is not int or rank not in (0, 1)
                or len(text.layers) != count or len(config.layer_types) != count
                or any(layer_type not in _LAYER_TYPES for layer_type in config.layer_types)
                or config._attn_implementation != "eager"
                or (config.rope_parameters or {}).get("rope_type") != "default"):
            raise CustodyError("Unsupported Qwen3.5 layer range, mask implementation, or rotary configuration.")
        self._torch = torch
        self._config = config
        self._rank = rank
        self._rotary = text.rotary_emb
        start, stop = (0, split) if rank == 0 else (split, count)
        self._layers = tuple((config.layer_types[i], text.layers[i]) for i in range(start, stop))
        self._edge = text.embed_tokens if rank == 0 else text.norm
        self._mask = {"full_attention": create_causal_mask, "linear_attention": create_recurrent_attention_mask}

    def forward(self, *, input_ids=None, hidden=None):
        """Accept only v8's text-only input, without custom positions, mask or cache."""
        torch = self._torch
        if self._rank == 0:
            if (hidden is not None or not isinstance(input_ids, torch.Tensor)
                    or input_ids.ndim != 2 or input_ids.dtype != torch.long):
                raise CustodyError("The early Qwen3.5 range requires rank-2 text token IDs only.")
            hidden = self._edge(input_ids)
        elif (input_ids is not None or not isinstance(hidden, torch.Tensor)
              or hidden.ndim != 3 or not hidden.is_floating_point()
              or hidden.shape[-1] != self._config.hidden_size):
            raise CustodyError("The tail Qwen3.5 range requires rank-3 boundary hidden state only.")
        batch, length, _ = hidden.shape
        if not 0 < length <= self._config.max_position_embeddings or not batch:
            raise CustodyError("Qwen3.5 text range received an invalid batch or sequence length.")
        position = torch.arange(length, device=hidden.device).view(1, 1, -1).expand(4, batch, -1)
        text_pos, rotary_pos = position[0], position[1:]
        masks = {name: self._mask[name](config=self._config, inputs_embeds=hidden,
                                       attention_mask=None, past_key_values=None, position_ids=text_pos)
                 for name in {name for name, _ in self._layers}}
        rotary = self._rotary(hidden, rotary_pos)
        for name, layer in self._layers:
            hidden = layer(hidden, position_embeddings=rotary, attention_mask=masks[name],
                           position_ids=text_pos, past_key_values=None, use_cache=False)
        return self._edge(hidden) if self._rank == 1 else hidden
