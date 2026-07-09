namespace JacRed.Infrastructure.Security
{
    public readonly struct JacRedAccessResult
    {
        public static JacRedAccessResult Allow { get; } = new(true, 0, false);

        public bool IsAllowed { get; }
        public int DenyStatusCode { get; }
        public bool SetPrivateNetworkHeaderOnDeny { get; }

        JacRedAccessResult(bool isAllowed, int denyStatusCode, bool setPrivateNetworkHeaderOnDeny)
        {
            IsAllowed = isAllowed;
            DenyStatusCode = denyStatusCode;
            SetPrivateNetworkHeaderOnDeny = setPrivateNetworkHeaderOnDeny;
        }

        public static JacRedAccessResult Deny(int statusCode, bool setPrivateNetworkHeader = true)
            => new(false, statusCode, setPrivateNetworkHeader);
    }
}
