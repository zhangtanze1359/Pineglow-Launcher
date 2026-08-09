using System;

namespace Pineglow.Launcher.Services;

public sealed class MicrosoftReauthenticationRequiredException : InvalidOperationException
{
	public MicrosoftReauthenticationRequiredException(string message)
		: base(message)
	{
	}
}
