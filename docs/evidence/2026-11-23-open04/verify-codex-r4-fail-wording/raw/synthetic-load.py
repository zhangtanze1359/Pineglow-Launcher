import math
from pathlib import Path
import sys
import time

mode, worker, workdir = sys.argv[1:]
directory = Path(workdir)
deadline = time.monotonic() + 300
if mode == "cpu":
    (directory / f"{worker}.ready").touch()
    while time.monotonic() < deadline:
        sum(math.sqrt(number) for number in range(100_000))
else:
    sample = directory / "io-iterations.txt"
    payload = bytes(1024 * 1024)
    data = directory / "io-data.bin"
    count = 0
    with sample.open("a", encoding="ascii") as progress:
        (directory / f"{worker}.ready").touch()
        while time.monotonic() < deadline:
            data.write_bytes(payload)
            assert len(data.read_bytes()) == len(payload)
            count += 1
            if count == 1 or count % 100 == 0:
                progress.write(f"io_iterations_at_{time.time():.3f}={count}\n")
                progress.flush()
            time.sleep(0.02)

