using System;

namespace MindWeaveServer.Utilities.Abstractions
{
    public interface IVerificationCodeService
    {
        string generateVerificationCode();
        DateTime getVerificationExpiryTime();
    }
}