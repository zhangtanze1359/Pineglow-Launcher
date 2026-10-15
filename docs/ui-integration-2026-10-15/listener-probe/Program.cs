using System;
using System.Net;
Console.WriteLine(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
using var listener=new HttpListener();
listener.Prefixes.Add("http://127.0.0.1:49374/");
try {listener.Start();Console.WriteLine("LISTENER_STARTED");}catch(Exception e){Console.WriteLine(e);Environment.ExitCode=1;}
try { Pineglow.Launcher.Services.LocalApiService.Start(49375);Console.WriteLine("PRODUCT_LISTENER_STARTED");Pineglow.Launcher.Services.LocalApiService.Stop(); }catch(Exception e){Console.WriteLine(e);Environment.ExitCode=1;}
