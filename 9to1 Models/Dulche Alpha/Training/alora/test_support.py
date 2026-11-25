"""Numeric software-only fixtures using the actual installed Qwen expert classes."""
import copy
from types import SimpleNamespace
import torch
from torch import nn
from torch.nn import functional as F
from transformers.models.qwen4_exp.modeling_qwen4_exp import Qwen4ExpTextSparseMoeBlock

from .adapters import attach_selected_experts
from .session import TrainingSession

PURPOSE = "software_test_only"
IDENTITY = {"base_model_sha256": "a" * 64, "dataset_manifest_sha256": "b" * 64,
            "profile_sha256": "c" * 64}


class NumericModel(nn.Module):
    def __init__(self):
        super().__init__()
        config = SimpleNamespace(num_experts=4, hidden_size=8, moe_intermediate_size=4,
                                 hidden_act="silu", num_experts_per_tok=2, _experts_implementation="eager",
                                 norm_topk_prob=True, shared_expert_intermediate_size=4)
        self.embedding = nn.Embedding(32, 8)
        self.blocks = nn.ModuleList([Qwen4ExpTextSparseMoeBlock(config) for _ in range(2)])
        self.head = nn.Linear(8, 32, bias=False)
        self.double()
        with torch.no_grad():
            for p in self.parameters():
                p.uniform_(-0.3, 0.3)

    def get_input_embeddings(self):
        return self.embedding

    def forward(self, input_ids=None, attention_mask=None, labels=None, use_cache=False, *, hidden=None):
        value = self.embedding(input_ids) if hidden is None else hidden
        for block in self.blocks:
            value = value + block(value)
        logits = self.head(value)
        loss = None if labels is None else F.cross_entropy(
            logits[:, :-1].reshape(-1, 32), labels[:, 1:].reshape(-1), ignore_index=-100)
        return SimpleNamespace(loss=loss, logits=logits)


def make_session(base=None, *, identity=None, purpose=PURPOSE):
    model = copy.deepcopy(base) if base is not None else NumericModel()
    attach_selected_experts(model, {"blocks.0.experts": [0, 1, 2, 3]}, rank=2, alpha=4)
    session = TrainingSession(model, identity=identity or IDENTITY, purpose=purpose, learning_rate=0.03)
    return session


def numeric_loss(session):
    # Arithmetic targets exist only to verify gradient/update software.
    tokens = torch.tensor([[1, 3, 9]], dtype=torch.long)
    labels = torch.tensor([[-100, 3, 9]], dtype=torch.long)
    return session.model(input_ids=tokens, labels=labels).loss
