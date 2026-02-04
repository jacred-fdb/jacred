using System;

namespace JacRed.Models.tParse
{
    public class TaskParse
    {
        #region TaskParse
        public TaskParse() { }

        public TaskParse(int _page)
        {
            page = _page;
        }
        #endregion

        public DateTime updateTime { get; set; }

        public int page { get; set; }
    }
}
