namespace ClutchExpert
{
    public class PlayerClutchCount
    {
        public PlayerClutchCount(int win = 0, bool complete = false)
        {
            _winCount = win;
            _complete = complete;
        }

        private int _winCount;
        private bool _complete;

        public int WinCount
        {
            get { return _winCount; }
            set { _winCount = value; }
        }

        public bool Complete
        {
            get { return _complete; }
            set { _complete = value; }
        }
    }

    public class PlayerData : PlayerClutchCount
    {
        public PlayerData(string achieve, string reset, int count, bool complete = true)
        {
            _timeAcheived = achieve;
            _timeReset = reset;

            WinCount = count;
            Complete = complete;
        }

        private string _timeAcheived;
        private string _timeReset;

        public string TimeAcheived
        {
            get { return _timeAcheived; }
            set { _timeAcheived = value; }
        }

        public string TimeReset
        {
            get { return _timeReset; }
            set { _timeReset = value; }
        }
    }
}
