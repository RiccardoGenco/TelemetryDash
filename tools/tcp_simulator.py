"""
TCP Telemetry Simulator
Sends binary telemetry frames to connected clients.

Usage: python tcp_simulator.py [--port PORT] [--interval MS]

Binary frame format:
  [4 byte] payload length (uint32 LE)
  [8 byte] timestamp (Unix ms, int64 LE)
  [1 byte] channel ID (0=TEMP_A1, 1=PRESS_B2, 2=VIB_C3, 3=FLOW_D4)
  [8 byte] value (double LE)
  [1 byte] quality flag (0=OK, 1=WARN, 2=ERR)
"""

import socket
import struct
import time
import math
import random
import argparse
import threading

CHANNELS = [
    {"id": 0, "name": "TEMP_A1",  "base": 65.0,   "amp": 15.0, "freq": 0.05, "noise": 2.0},
    {"id": 1, "name": "PRESS_B2", "base": 1013.0,  "amp": 50.0, "freq": 0.03, "noise": 5.0},
    {"id": 2, "name": "VIB_C3",   "base": 0.5,     "amp": 0.3,  "freq": 0.15, "noise": 0.05},
    {"id": 3, "name": "FLOW_D4",  "base": 120.0,   "amp": 20.0, "freq": 0.08, "noise": 3.0},
]

def generate_value(channel, elapsed):
    sine = channel["amp"] * math.sin(2 * math.pi * channel["freq"] * elapsed)
    noise = (random.random() * 2 - 1) * channel["noise"]
    value = channel["base"] + sine + noise

    # Occasional spike (2% chance)
    if random.random() < 0.02:
        spike = (1 if random.random() > 0.5 else -1) * channel["amp"] * 1.5
        value += spike

    return value

def build_frame(channel_id, value, quality=0):
    timestamp_ms = int(time.time() * 1000)
    # Payload: timestamp(8) + channel(1) + value(8) + quality(1) = 18 bytes
    payload = struct.pack("<qBdB", timestamp_ms, channel_id, value, quality)
    header = struct.pack("<I", len(payload))
    return header + payload

def handle_client(conn, addr, interval_ms):
    print(f"[+] Client connected: {addr}")
    start_time = time.time()
    try:
        while True:
            elapsed = time.time() - start_time
            for ch in CHANNELS:
                value = generate_value(ch, elapsed)
                quality = 0
                deviation = abs(value - ch["base"]) / ch["amp"]
                if deviation > 1.5:
                    quality = 2
                elif deviation > 1.2:
                    quality = 1

                frame = build_frame(ch["id"], value, quality)
                conn.sendall(frame)

            time.sleep(interval_ms / 1000.0)
    except (BrokenPipeError, ConnectionResetError, OSError):
        print(f"[-] Client disconnected: {addr}")
    finally:
        conn.close()

def main():
    parser = argparse.ArgumentParser(description="TCP Telemetry Simulator")
    parser.add_argument("--port", type=int, default=5000, help="TCP port (default: 5000)")
    parser.add_argument("--interval", type=int, default=500, help="Send interval in ms (default: 500)")
    args = parser.parse_args()

    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind(("0.0.0.0", args.port))
    server.listen(5)

    print(f"[*] TCP Simulator listening on port {args.port} (interval: {args.interval}ms)")
    print(f"[*] Channels: {', '.join(ch['name'] for ch in CHANNELS)}")

    try:
        while True:
            conn, addr = server.accept()
            t = threading.Thread(target=handle_client, args=(conn, addr, args.interval), daemon=True)
            t.start()
    except KeyboardInterrupt:
        print("\n[*] Shutting down")
    finally:
        server.close()

if __name__ == "__main__":
    main()
