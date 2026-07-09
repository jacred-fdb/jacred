using System;
using System.Collections.Generic;

namespace JacRed.Configuration
{
    public sealed class ConfigSourceInfo
    {
        public string path { get; set; }
        public string format { get; set; }
        public bool exists { get; set; }
        public DateTime? lastModifiedUtc { get; set; }
    }

    public sealed class ConfigValidationResult
    {
        public bool ok { get; set; }
        public string error { get; set; }
        public List<string> warnings { get; set; } = new List<string>();
        public List<string> errors { get; set; } = new List<string>();
    }
}
