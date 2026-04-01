"""
zmq_sniffer.py  –  Sumo2Unity diagnostic tool
================================================
Run this WHILE the Sumo2UnityTool.exe is running (before or during Play mode).
It subscribes to the same ZeroMQ PUB socket (port 5556) and prints every
unique message type + one full raw sample.

Usage:
    pip install pyzmq        (one-time install)
    python zmq_sniffer.py

Press Ctrl+C to stop.
"""

import zmq, json, sys

ENDPOINT = "tcp://localhost:5556"
MAX_SAMPLE_LEN = 500   # chars to print per sample

def main():
    ctx = zmq.Context()
    sub = ctx.socket(zmq.SUB)
    sub.connect(ENDPOINT)
    sub.setsockopt_string(zmq.SUBSCRIBE, "")   # subscribe to everything
    print(f"[Sniffer] Listening on {ENDPOINT} ... (Ctrl+C to stop)\n")

    seen_types = {}   # type -> count
    msg_count  = 0

    try:
        while True:
            raw = sub.recv_string()
            msg_count += 1

            try:
                obj  = json.loads(raw)
                mtype = obj.get("type", "<no-type-field>")
            except Exception:
                mtype = "<invalid-json>"
                obj   = None

            if mtype not in seen_types:
                seen_types[mtype] = 0
                print(f"\n{'='*60}")
                print(f"  NEW TYPE SEEN: '{mtype}'")
                print(f"  Sample (first {MAX_SAMPLE_LEN} chars):")
                print(f"  {raw[:MAX_SAMPLE_LEN]}")
                print(f"{'='*60}\n")
                sys.stdout.flush()

            seen_types[mtype] += 1

            # Progress heartbeat every 100 messages
            if msg_count % 100 == 0:
                summary = ", ".join(f"'{t}':{c}" for t,c in seen_types.items())
                print(f"[Sniffer] {msg_count} msgs received. Types so far: {summary}")
                sys.stdout.flush()

    except KeyboardInterrupt:
        print("\n[Sniffer] Stopped by user.")
        summary = "\n".join(f"  '{t}': {c} messages" for t,c in seen_types.items())
        print(f"\n[Sniffer] Final summary:\n{summary}")

if __name__ == "__main__":
    main()
