#!/usr/bin/env python3
"""Unit tests for cron/generate.py max_time defaults and crontab output."""

from __future__ import annotations

import importlib.util
import tempfile
import unittest
from pathlib import Path

CRON_DIR = Path(__file__).resolve().parents[3] / "cron"
GEN_PATH = CRON_DIR / "generate.py"


def load_generate():
    spec = importlib.util.spec_from_file_location("jacred_cron_generate", GEN_PATH)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(mod)
    return mod


class GenerateMaxTimeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.gen = load_generate()

    def test_ack_defaults_are_60(self):
        self.assertEqual(60, self.gen.default_max_time("/cron/rutor/ParseAllTask"))
        self.assertEqual(60, self.gen.default_max_time("/cron/rutor/UpdateTasksParse"))
        self.assertEqual(60, self.gen.default_max_time("/jsondb/save"))

    def test_parse_default_is_900(self):
        self.assertEqual(900, self.gen.default_max_time("/cron/rutor/parse"))

    def test_explicit_max_time_wins(self):
        job = {"path": "/cron/rutor/ParseAllTask", "max_time": 120}
        self.assertEqual(120, self.gen.resolve_max_time(job))

    def test_generate_writes_env_and_crontab(self):
        yaml = """
base_url: http://127.0.0.1:9117
jobs:
  - name: rutor-ParseAllTask
    schedule: "30 * * * *"
    path: /cron/rutor/ParseAllTask
  - name: rutor-parse
    schedule: "*/15 * * * *"
    path: /cron/rutor/parse
""".lstrip()

        with tempfile.TemporaryDirectory() as tmp:
            cron_dir = Path(tmp)
            (cron_dir / "jobs.yaml").write_text(yaml, encoding="utf-8")
            (cron_dir / "run-job.sh").write_text("#!/bin/bash\n", encoding="utf-8")
            count = self.gen.generate(cron_dir)
            self.assertEqual(2, count)

            env_ack = (cron_dir / "generated" / "jacred-job-rutor-ParseAllTask.env").read_text(encoding="utf-8")
            env_parse = (cron_dir / "generated" / "jacred-job-rutor-parse.env").read_text(encoding="utf-8")
            self.assertIn("MAX_TIME=60", env_ack)
            self.assertIn("MAX_TIME=900", env_parse)

            # No systemd units anymore.
            self.assertEqual([], list((cron_dir / "generated").glob("*.service")))
            self.assertEqual([], list((cron_dir / "generated").glob("*.timer")))

            crontab = (cron_dir / "generated" / "crontab").read_text(encoding="utf-8")
            self.assertIn("run-job.sh rutor-ParseAllTask", crontab)
            self.assertIn("run-job.sh rutor-parse", crontab)
            self.assertIn("30 * * * *", crontab)
            self.assertNotIn("curl -s", crontab)
            self.assertIn("/opt/jacred/cron/run-job.sh", crontab)


if __name__ == "__main__":
    unittest.main()
