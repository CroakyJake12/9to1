"""Boundary and accounting invariants for the candidate's pure pool planner."""
from dataclasses import replace
import random
import unittest

from pooled_budget import (
    Allocation, DISPLAY_RESERVED_BYTES, MAX_INTEGER, MIB, PlanningError,
    PoolPolicy, RTX_UUID, Snapshot, budget_pool, plan_admission,
)

V100_UUID = "GPU-00000000-1111-2222-3333-444444444444"
NOW = 1_795_594_166_000_000_000
POLICY = PoolPolicy("cuda:RTX", V100_UUID, "rpc:127.0.0.1:19110/device0", 5_000_000_000)


def samples(*, rtx_total=16 * 1024**3, rtx_external=400_000_000, rtx_owned=10_000_000_000,
            v100_total=16 * 1024**3, v100_external=200_000_000, v100_owned=8_000_000_000):
    return (
        Snapshot(RTX_UUID, POLICY.rtx_endpoint, NOW, rtx_total,
                 rtx_external + rtx_owned, rtx_owned, True, True),
        Snapshot(V100_UUID, POLICY.v100_endpoint, NOW, v100_total,
                 v100_external + v100_owned, v100_owned, True, True),
    )


class BudgetTests(unittest.TestCase):
    def test_display_usage_is_not_subtracted_twice(self):
        budget = budget_pool(POLICY, samples(), now_ns=NOW)
        rtx, v100 = budget.devices
        self.assertEqual(rtx.owned_limit_bytes, 16 * 1024**3 - 1_400_000_000)
        self.assertEqual(rtx.additional_headroom_bytes, rtx.owned_limit_bytes - 10_000_000_000)
        self.assertEqual(rtx.unconsumed_reserve_bytes, 1_000_000_000)
        self.assertEqual(v100.owned_limit_bytes, 16 * 1024**3 - 200_000_000)
        self.assertEqual(v100.display_reserved_bytes, 0)
        self.assertEqual(budget.display_reserved_bytes, 1_400_000_000)
        self.assertFalse(budget.hard_reservation_supported)
        self.assertFalse(budget.fit_target_is_hard_limit)

    def test_more_external_usage_than_reserve_reduces_capacity(self):
        rtx = budget_pool(POLICY, samples(rtx_external=2_000_000_000), now_ns=NOW).devices[0]
        self.assertEqual(rtx.owned_limit_bytes, rtx.total_bytes - 2_000_000_000)
        self.assertEqual(rtx.unconsumed_reserve_bytes, 0)
        self.assertEqual(rtx.fit_target_mib, 0)

    def test_mib_rounding_does_not_change_exact_policy(self):
        rtx = budget_pool(POLICY, samples(rtx_external=0), now_ns=NOW).devices[0]
        self.assertEqual(rtx.fit_target_mib, 1336)
        self.assertEqual(rtx.fit_target_bytes, 1_400_897_536)
        self.assertEqual(rtx.fit_rounding_bytes, 897_536)
        self.assertEqual(rtx.display_reserved_bytes, DISPLAY_RESERVED_BYTES)
        for remaining in (0, 1, MIB - 1, MIB, MIB + 1):
            with self.subTest(remaining=remaining):
                snap = samples(rtx_external=DISPLAY_RESERVED_BYTES - remaining)
                rtx = budget_pool(POLICY, snap, now_ns=NOW).devices[0]
                self.assertGreaterEqual(rtx.fit_target_bytes, remaining)
                self.assertLess(rtx.fit_target_bytes - remaining, MIB)

    def test_measured_capacity_not_nominal_32_gb(self):
        budget = budget_pool(POLICY, samples(rtx_total=16_303 * MIB, v100_total=16_383 * MIB), now_ns=NOW)
        self.assertEqual(budget.reported_total_bytes, 32_686 * MIB)
        self.assertNotEqual(budget.reported_total_bytes, 32_000_000_000)
        self.assertEqual(budget.physical_devices, (RTX_UUID, V100_UUID))

    def test_staleness_boundary_and_future(self):
        a, b = samples()
        budget = budget_pool(POLICY, (replace(a, observed_at_ns=NOW - POLICY.max_age_ns), b), now_ns=NOW)
        self.assertEqual(budget.observed_at_ns, NOW - POLICY.max_age_ns)
        self.assertEqual(budget.evaluated_at_ns, NOW)
        for stamp in (NOW - POLICY.max_age_ns - 1, NOW + 1):
            with self.subTest(stamp=stamp), self.assertRaises(PlanningError):
                budget_pool(POLICY, (replace(a, observed_at_ns=stamp), b), now_ns=NOW)

    def test_missing_duplicate_foreign_or_unhealthy_devices_fail(self):
        a, b = samples()
        cases = (
            (a,), (a, a), (a, b, b),
            (a, replace(b, gpu_uuid="GPU-11111111-1111-2222-3333-444444444444")),
            (a, replace(b, endpoint_id="rpc:wrong")),
            (a, replace(b, endpoint_ready=False)),
            (a, replace(b, healthy=False)),
            (a, replace(b, healthy="true")),
        )
        for case in cases:
            with self.subTest(case=case), self.assertRaises(PlanningError):
                budget_pool(POLICY, case, now_ns=NOW)

    def test_invalid_numbers_fail_instead_of_producing_capacity(self):
        a, b = samples()
        changes = (
            {"total_bytes": 0}, {"used_bytes": -1}, {"used_bytes": a.total_bytes + 1},
            {"owned_bytes": a.used_bytes + 1}, {"total_bytes": True},
            {"used_bytes": 100.5}, {"owned_bytes": MAX_INTEGER + 1},
            {"observed_at_ns": False},
            {"total_bytes": DISPLAY_RESERVED_BYTES - 1, "used_bytes": 0, "owned_bytes": 0},
        )
        for change in changes:
            with self.subTest(change=change), self.assertRaises(PlanningError):
                budget_pool(POLICY, (replace(a, **change), b), now_ns=NOW)

    def test_policy_rejects_identity_aliases_and_missing_age(self):
        for policy in (
            replace(POLICY, v100_uuid=RTX_UUID[:4] + RTX_UUID[4:].upper()),
            replace(POLICY, v100_endpoint=POLICY.rtx_endpoint),
            replace(POLICY, max_age_ns=0),
            replace(POLICY, rtx_endpoint=""),
            replace(POLICY, v100_uuid="GPU-0"),
        ):
            with self.subTest(policy=policy), self.assertRaises(PlanningError):
                budget_pool(policy, samples(), now_ns=NOW)

    def test_accounting_invariants_across_varied_external_and_owned_usage(self):
        rng = random.Random(5080100)
        for _ in range(150):
            total = rng.randrange(2_000_000_000, 18_000_000_000)
            used = rng.randrange(total + 1)
            owned = rng.randrange(used + 1)
            snap = samples(rtx_total=total, rtx_external=used - owned, rtx_owned=owned)
            d = budget_pool(POLICY, snap, now_ns=NOW).devices[0]
            self.assertEqual(d.external_bytes + d.owned_bytes, d.used_bytes)
            self.assertEqual(d.owned_limit_bytes + max(d.external_bytes, DISPLAY_RESERVED_BYTES), d.total_bytes)
            self.assertEqual(d.owned_limit_bytes - d.owned_bytes,
                             d.additional_headroom_bytes - d.current_deficit_bytes)
            self.assertLessEqual(d.additional_headroom_bytes, d.total_bytes - d.used_bytes)
            self.assertGreaterEqual(d.fit_target_bytes, d.unconsumed_reserve_bytes)
            self.assertLess(d.fit_rounding_bytes, MIB)

    def test_aggregate_size_overflow_is_rejected(self):
        snap = samples(rtx_total=MAX_INTEGER, v100_total=MAX_INTEGER,
                       rtx_owned=0, rtx_external=0, v100_owned=0, v100_external=0)
        with self.assertRaises(PlanningError):
            budget_pool(POLICY, snap, now_ns=NOW)


class AdmissionTests(unittest.TestCase):
    def request(self, rtx, v100):
        return {RTX_UUID: Allocation(rtx, 0, 0), V100_UUID: Allocation(v100, 0, 0)}

    def test_exact_boundary_claims_both_and_one_byte_excess_claims_neither(self):
        snap = samples()
        budget = budget_pool(POLICY, snap, now_ns=NOW)
        ra, va = (d.owned_limit_bytes for d in budget.devices)
        plan = plan_admission(POLICY, snap, self.request(ra, va), now_ns=NOW)
        self.assertTrue(plan.admissible)
        self.assertEqual(plan.device_claims, (RTX_UUID, V100_UUID))
        self.assertFalse(plan.performs_acquisition)
        self.assertFalse(plan.hard_reservation_supported)
        for requested in ((ra + 1, va), (ra, va + 1)):
            with self.subTest(requested=requested):
                plan = plan_admission(POLICY, snap, self.request(*requested), now_ns=NOW)
                self.assertFalse(plan.admissible)
                self.assertEqual(plan.device_claims, ())
                self.assertEqual(sum(d.requested_deficit_bytes for d in plan.devices), 1)

    def test_weights_that_fit_cannot_hide_kv_and_compute_excess(self):
        snap = samples()
        cap = budget_pool(POLICY, snap, now_ns=NOW).devices[0].owned_limit_bytes
        request = {RTX_UUID: Allocation(cap - 100, 70, 30, 1), V100_UUID: Allocation(1, 1, 1)}
        plan = plan_admission(POLICY, snap, request, now_ns=NOW)
        self.assertFalse(plan.admissible)
        self.assertEqual(plan.devices[0].requested_total_bytes, cap + 1)
        self.assertEqual(plan.devices[0].requested_deficit_bytes, 1)

    def test_spare_v100_capacity_does_not_cover_rtx_deficit(self):
        snap = samples(v100_owned=0, v100_external=0)
        cap = budget_pool(POLICY, snap, now_ns=NOW).devices[0].owned_limit_bytes
        plan = plan_admission(POLICY, snap, self.request(cap + 1, 0), now_ns=NOW)
        self.assertLess(sum(d.requested_total_bytes for d in plan.devices), plan.budget.owned_limit_bytes)
        self.assertFalse(plan.admissible)
        self.assertEqual(plan.device_claims, ())

    def test_current_reserve_violation_blocks_even_smaller_desired_plan(self):
        total = 16 * 1024**3
        snap = samples(rtx_owned=total - DISPLAY_RESERVED_BYTES + 1, rtx_external=0)
        plan = plan_admission(POLICY, snap, self.request(1, 1), now_ns=NOW)
        self.assertEqual(plan.budget.current_deficit_bytes, 1)
        self.assertEqual(plan.devices[0].requested_deficit_bytes, 0)
        self.assertFalse(plan.admissible)
        self.assertEqual(plan.device_claims, ())

    def test_existing_owned_memory_is_reused_not_added_twice(self):
        snap = samples()
        plan = plan_admission(POLICY, snap, self.request(11_000_000_000, 8_000_000_000), now_ns=NOW)
        self.assertTrue(plan.admissible)
        self.assertEqual(tuple(d.additional_bytes for d in plan.devices), (1_000_000_000, 0))

    def test_each_physical_gpu_needs_an_explicit_valid_request(self):
        for requests in (
            {RTX_UUID: Allocation(1, 1, 1)},
            self.request(-1, 0), self.request(True, 0), self.request(1.5, 0),
            {RTX_UUID: Allocation(MAX_INTEGER, 1, 0), V100_UUID: Allocation(0, 0, 0)},
        ):
            with self.subTest(requests=requests), self.assertRaises(PlanningError):
                plan_admission(POLICY, samples(), requests, now_ns=NOW)

    def test_plan_cannot_reuse_stale_measurements(self):
        with self.assertRaises(PlanningError):
            plan_admission(POLICY, samples(), self.request(1, 1), now_ns=NOW + POLICY.max_age_ns + 1)

    def test_invalid_request_container_has_clear_failure(self):
        with self.assertRaisesRegex(PlanningError, "mapping"):
            plan_admission(POLICY, samples(), None, now_ns=NOW)


if __name__ == "__main__":
    unittest.main()
