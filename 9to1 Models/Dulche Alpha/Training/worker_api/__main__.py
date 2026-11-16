"""Serve one bounded request from the local Studio process."""

import argparse
from pathlib import Path
import sys

from .bridge import WorkerApi
from .protocol import RESPONSE_BYTES, WorkerError, encode_event, read_request


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--operator-config", type=Path, required=True)
    options = parser.parse_args()

    def emit(event, **fields):
        line = encode_event(event, **fields)
        if len(line) > RESPONSE_BYTES:
            raise WorkerError("response_too_large", "The worker event exceeds its output limit.")
        sys.stdout.buffer.write(line + b"\n")
        sys.stdout.buffer.flush()

    try:
        command, data = read_request(sys.stdin.buffer)
        api = WorkerApi(options.operator_config)
        if command == "status":
            result = api.status(data)
        elif command == "pause":
            result = api.pause(data)
        elif command == "list":
            result = api.list(*data)
        else:
            result = api.execute(data, emit)
        emit("result", result=result)
        return 0
    except WorkerError as error:
        emit("error", code=error.code, message=str(error))
    except BrokenPipeError:
        return 1
    except (OSError, ValueError, KeyError, TypeError, RecursionError):
        emit("error", code="local_worker_unavailable", message="The local worker could not complete this command.")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
