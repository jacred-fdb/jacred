using JacRed.Infrastructure.Persistence;
using System;

namespace JacRed.Models
{
    public class WriteTaskModel
    {
        public FileDB db { get; set; }

        public DateTime lastread { get; set; }

        public DateTime create { get; set; } = DateTime.Now;

        public int countread { get; set; }

        public int openconnection { get; set; }
    }
}
