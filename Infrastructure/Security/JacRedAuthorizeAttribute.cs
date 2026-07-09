using System;

namespace JacRed.Infrastructure.Security
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class JacRedAuthorizeAttribute : Attribute
    {
        public JacRedAccessPolicy Policy { get; }

        public JacRedAuthorizeAttribute(JacRedAccessPolicy policy)
        {
            Policy = policy;
        }
    }
}
