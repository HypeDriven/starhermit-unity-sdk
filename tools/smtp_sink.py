#!/usr/bin/env python3
"""A local SMTP server that delivers nowhere: each message lands, decoded, as a file in a directory.

The authenticated live contract tests sign in the way a player does - register a key, then redeem the
link the deployment emails - so they need to read that email. Point a throwaway deployment's SMTP
settings at this sink and the tests at its directory (STARHERMIT_TEST_MAILBOX):

    python3 tools/smtp_sink.py --port 2526 --dir /tmp/starhermit-mail

Each message is written as <time>-<n>.txt: its To and Subject headers, a blank line, then every text
part decoded (quoted-printable and base64 undone), so a test can find its link with a plain search.
Plain SMTP only - no TLS, no AUTH - and it binds to loopback. It is a test fixture, not a mail server.
"""
import argparse
import email
import email.policy
import os
import socketserver
import time


class SinkHandler(socketserver.StreamRequestHandler):
    def reply(self, line: str) -> None:
        self.wfile.write((line + "\r\n").encode("ascii"))
        self.wfile.flush()

    def handle(self) -> None:
        self.reply("220 starhermit-sdk-smtp-sink ready")
        while True:
            raw = self.rfile.readline()
            if not raw:
                return
            command = raw.decode("utf-8", "replace").strip()
            verb = command.split(" ", 1)[0].upper()
            if verb in ("EHLO", "HELO"):
                # No extensions: the client must not try STARTTLS or AUTH against a fixture.
                self.reply("250 starhermit-sdk-smtp-sink")
            elif verb in ("MAIL", "RCPT", "RSET", "NOOP"):
                self.reply("250 OK")
            elif verb == "DATA":
                self.reply("354 End data with <CR><LF>.<CR><LF>")
                lines = []
                while True:
                    line = self.rfile.readline()
                    if not line or line in (b".\r\n", b".\n"):
                        break
                    if line.startswith(b".."):
                        line = line[1:]
                    lines.append(line)
                self.server.deliver(b"".join(lines))
                self.reply("250 OK: delivered to the sink")
            elif verb == "QUIT":
                self.reply("221 Bye")
                return
            else:
                self.reply("502 Command not implemented")


class SinkServer(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True

    def __init__(self, address, directory: str):
        super().__init__(address, SinkHandler)
        self.directory = directory
        self.count = 0

    def deliver(self, data: bytes) -> None:
        message = email.message_from_bytes(data, policy=email.policy.default)
        parts = []
        for part in message.walk():
            if part.get_content_maintype() == "text":
                parts.append(part.get_content())
        self.count += 1
        name = f"{time.time_ns()}-{self.count}.txt"
        temporary = os.path.join(self.directory, "." + name)
        with open(temporary, "w", encoding="utf-8") as handle:
            handle.write(f"To: {message.get('To', '')}\nSubject: {message.get('Subject', '')}\n\n")
            handle.write("\n".join(parts))
        # Renamed into place, so a reader never sees half a message.
        os.replace(temporary, os.path.join(self.directory, name))
        print(f"delivered {name} to {message.get('To', '')}", flush=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--port", type=int, default=2526)
    parser.add_argument("--dir", required=True, help="directory each message is written to")
    arguments = parser.parse_args()
    os.makedirs(arguments.dir, exist_ok=True)
    with SinkServer(("127.0.0.1", arguments.port), arguments.dir) as server:
        print(f"SMTP sink on 127.0.0.1:{arguments.port}, writing to {arguments.dir}", flush=True)
        server.serve_forever()


if __name__ == "__main__":
    main()
