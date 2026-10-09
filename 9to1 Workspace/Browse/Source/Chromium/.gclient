# Source pin is in ../chromium-source.lock.json and the src gitlink.
# Sync with: gclient sync --nohooks --no-history --revision src@<locked-commit>
solutions = [
    {
        "name": "src",
        "url": "https://chromium.googlesource.com/chromium/src.git",
        "managed": False,
        "deps_file": "DEPS",
        "custom_deps": {},
        "custom_vars": {},
    },
]
