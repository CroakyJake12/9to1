"""Explicit inference policy; training and pooled operating envelopes stay conservative."""
def active_cooling_rule(rule, role):
    if not isinstance(rule, dict) or rule.get("thermal_policy_version") != "active-cooling-v1":
        return False
    expected = {"warn_c": 75, "stop_c": 80, "core_grace_s": 10,
                "core_emergency_c": 82 if role == "v100" else 85,
                "memory_stop_c": 68 if role == "v100" else 80,
                "allow_simulated": False, "jump_min_c": 75}
    if role == "v100":
        expected.update(power_limit_w=165, rise_min_c=75, jump_c=10,
                        rise_c_per_min=15, telemetry_timeout_s=15, unreadable_timeout_s=5)
    return all(type(rule.get(k)) in (int, float) and rule[k] == v
               if type(v) in (int, float) else rule.get(k) is v
               for k,v in expected.items())

def runtime_core_limit(snapshot):
    # A policy change requires a fresh watchdog snapshot, not an edited config alone.
    p = snapshot.get("thermal_policy")
    if p == {"version":"active-cooling-v1","warn_c":75,"stop_c":80,"core_grace_s":10,
             "core_emergency_c":82,"memory_stop_c":68}:
        return 82
    return 68
