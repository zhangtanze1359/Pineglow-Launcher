using System;

namespace Pineglow.Launcher.Services;

public sealed class MicrosoftBrowserAuthorizationException : InvalidOperationException
{
	public string Diagnostic { get; }

	public MicrosoftBrowserAuthorizationException(string message, string stage, int? httpStatus = null)
		: base(message)
	{
		Diagnostic = (httpStatus.HasValue ? $"stage={stage}; http_status={httpStatus.Value}" : ("stage=" + stage));
	}
}
