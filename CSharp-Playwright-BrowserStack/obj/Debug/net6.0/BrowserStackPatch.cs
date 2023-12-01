#pragma warning disable
using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using System.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading;
using System.Linq;
using System.Net.Http;
using NLog;
using Serilog;
using Serilog;
using NLog.Config;
using NLog.Targets;
using TestObservability.Serilog.Sink;
using TestObservability.NLog.Appender;
using TestObservability.ConsoleAppender;



using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using NUnit.Framework;




internal static class Initializer
{
    public static List<MethodBase> patchMethods = new List<MethodBase>();
    [ModuleInitializer]
    internal static void Run() {
        try
        {
            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += new UnhandledExceptionEventHandler(VanilaErrorHandler);
        }
        catch {}

        try
        {
            int index = int.Parse(Environment.GetEnvironmentVariable("index") ?? "0");
            string jsonText = File.ReadAllText(Environment.GetEnvironmentVariable("capabilitiesPath"));

            if (jsonText != null)
            {
                JArray json = JArray.Parse(jsonText);
                if(json.Count > 0)
                {
                    JObject jsonIndexed = (JObject)json[index];
                    BrowserStackSDK.Automation.Context.capabilitiesJson = jsonIndexed;
                }
            }
        }
        catch{}

        Assembly assembly = Assembly.GetExecutingAssembly();
        BrowserStackSDK.Automation.Context.executingAssembly = assembly;
        string[] attributes = { "NUnit.Framework.TestAttribute", "NUnit.Framework.TestCaseAttribute", "NUnit.Framework.TestCaseSourceAttribute", "NUnit.Framework.TheoryAttribute" };
        var allTypes = assembly.GetTypes();
        foreach (var type in allTypes)
        {
            if (type.IsClass)
            {
                foreach (var method in type.GetMethods())
                {
                    foreach (var att in method.CustomAttributes)
                    {
                       if (attributes.Contains(att.Constructor.DeclaringType.ToString()) && (method.DeclaringType == method.ReflectedType))
                        {
                            patchMethods.Add(method);
                        }
                    }
                }
            }
        }
        if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JWT_TOKEN")))
            Console.SetOut(new ConsoleAppender());
        BrowserstackPatcher.DoPatching();
    }

    static void VanilaErrorHandler(object sender, UnhandledExceptionEventArgs args)
    {
        Exception e = (Exception)args.ExceptionObject;
        string filePath = Path.Join(Path.GetTempPath(), ".browserstack", "vanilaErrorFile_" + Environment.GetEnvironmentVariable("index"));
        var platformDetails = Environment.GetEnvironmentVariable("browserName") + " " + Environment.GetEnvironmentVariable("osVersion") + " " + Environment.GetEnvironmentVariable("os") + " " + Environment.GetEnvironmentVariable("browserVersion") ;
        string[] fileContents = { platformDetails + "\n------------\n" + e.Message + "\n" + e.GetBaseException() + "\n"};
        File.WriteAllLines(filePath, fileContents);
    }
}

public class BrowserstackPatcher
{
    //public static Configuration configs;
    // make sure DoPatching() is called at start either by
    // the mod loader or by your injector
    public static void DoPatching()
    {
        var harmony = new Harmony("com.browserstack.patch");
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        if(Environment.GetEnvironmentVariable("VSTEST_HOST_DEBUG") != "1")
        {
            foreach (var method in Initializer.patchMethods)
            {
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(PatchTest).GetMethod(nameof(PatchTest.Prefix))), finalizer: new HarmonyMethod(typeof(PatchTest).GetMethod(nameof(PatchTest.Finalizer))));
            }

            
            
        }

    }
}

class BrowserStackException : Exception
{
    private string oldStackTrace;

    public BrowserStackException(string message, string stackTrace) : base(message)
    {
        this.oldStackTrace = stackTrace;
    }


    public override string StackTrace
    {
        get
        {
            return this.oldStackTrace;
        }
    }
}

class Store {
    public static void PersistHubUrl(string optimalHubUrl) {
        try {
            string filePath = Path.Combine(Path.GetTempPath(), ".browserstack", "hubUrlList.json");
            if (File.Exists(filePath)) {
                string hubUrls = File.ReadAllText(filePath);
                hubUrls += $"; {optimalHubUrl}";
                File.WriteAllText(filePath, hubUrls);
                return;
            }
            File.WriteAllText(filePath, optimalHubUrl);
            return;
        } catch {}
    }
}




[HarmonyPatch]
class BrowserTypeLaunchPatch
{
    static MethodBase TargetMethod()
    {
        Assembly assembly = typeof(Playwright).Assembly;
        return assembly.GetType("Microsoft.Playwright.Core.BrowserType").GetMethod("LaunchAsync");
    }

    static bool Prefix(ref Task<IBrowser> __result, IBrowserType __instance, BrowserTypeLaunchOptions options = default)
    {
        int index = int.Parse(Environment.GetEnvironmentVariable("index"));
        var isLocal = Environment.GetEnvironmentVariable("isLocal");
        var localIdentifier = Environment.GetEnvironmentVariable("localIdentifier");
        var proxy = Environment.GetEnvironmentVariable("proxy");
        Dictionary<string, Object> capabilities = new Dictionary<string, Object>();

        if (isLocal == "true")
        {
            capabilities.Add("browserstack.local", isLocal);
            if (localIdentifier != "")
                capabilities.Add("browserstack.localIdentifier", localIdentifier);
        }

        String jsonText = null;
        try
        {
            jsonText = File.ReadAllText(Environment.GetEnvironmentVariable("capabilitiesPath"));
        }
        catch
        { }

        if (jsonText != null)
        {
            JArray json = JArray.Parse(jsonText);
            if (json.Count > 0)
            {
                JObject jsonIndexed = (JObject)json[index];
                foreach (var item in jsonIndexed)
                {
                    try
                    {
                        if (item.Value != null)
                        {
                            capabilities.Add(item.Key, item.Value);
                        }
                    }
                    catch { }
                }
            }

        }

        string capsJson = JsonConvert.SerializeObject(capabilities);
        string cdpUrl = "wss://cdp.browserstack.com/playwright?caps=" + Uri.EscapeDataString(capsJson);
        __result = __instance.ConnectAsync(cdpUrl, null);
        return false;
    }
}

[HarmonyPatch]
class BrowserTypeConnectPatch
{
    static MethodBase TargetMethod()
    {
        Assembly assembly = typeof(Playwright).Assembly;
        return assembly.GetType("Microsoft.Playwright.Core.BrowserType").GetMethod("ConnectAsync");
    }

    static void Prefix(ref string wsEndpoint, BrowserTypeLaunchOptions options = default)
    {
        int index = int.Parse(Environment.GetEnvironmentVariable("index"));
        var isLocal = Environment.GetEnvironmentVariable("isLocal");
        var localIdentifier = Environment.GetEnvironmentVariable("localIdentifier");
        var proxy = Environment.GetEnvironmentVariable("proxy");
        Dictionary<string, Object> capabilities = new Dictionary<string, Object>();

        if (isLocal == "true")
        {
            capabilities.Add("browserstack.local", isLocal);
            if (localIdentifier != "")
                capabilities.Add("browserstack.localIdentifier", localIdentifier);
        }

        String jsonText = null;
        try
        {
            jsonText = File.ReadAllText(Environment.GetEnvironmentVariable("capabilitiesPath"));
        }
        catch
        { }

        if (jsonText != null)
        {
            JArray json = JArray.Parse(jsonText);
            if (json.Count > 0)
            {
                JObject jsonIndexed = (JObject)json[index];
                foreach (var item in jsonIndexed)
                {
                    try
                    {
                        if (item.Value != null)
                        {
                            capabilities.Add(item.Key, item.Value);
                        }
                    }
                    catch { }
                }
            }

        }

        string capsJson = JsonConvert.SerializeObject(capabilities);
        wsEndpoint = "wss://cdp.browserstack.com/playwright?caps=" + Uri.EscapeDataString(capsJson);
    }
}

[HarmonyPatch]
class BrowserContextPatch
{
    public static Dictionary<string, IPage> pages__ = new Dictionary<string, IPage>();
    public static Dictionary<string, List<string>> errorMessagesList = new Dictionary<string, List<string>>();

    static MethodBase TargetMethod()
    {
        Assembly assembly = typeof(Playwright).Assembly;
        return assembly.GetType("Microsoft.Playwright.Core.BrowserContext").GetMethod("NewPageAsync");
    }

    public static async void Postfix(Task<IPage> __result)
    {
        try
        {
            IPage page = await __result;
            pages__[TestContext.CurrentContext.Test.FullName] = page;

        }  catch { }
    }
}

[HarmonyPatch]
class ClosePatch
{
    public static void MarkSessionStatus(string status, string reason, IPage page)
    {
        JObject executorObject = new JObject();
        JObject argumentsObject = new JObject
        {
            { "status", status },
            { "reason", reason }
        };
        executorObject.Add("action", "setSessionStatus");
        executorObject.Add("arguments", argumentsObject);
        _ = page.EvaluateAsync("_ => {}", "browserstack_executor: " + executorObject.ToString()).Result;
    }

    static MethodBase TargetMethod()
    {
        Assembly assembly = typeof(Playwright).Assembly;
        return assembly.GetType("Microsoft.Playwright.Core.Browser").GetMethod("CloseAsync");
    }


    public static bool Prefix()
    {
        try
        {
            var page = BrowserContextPatch.pages__.GetValueOrDefault(TestContext.CurrentContext.Test.FullName, null);
            if (page != null)
            {
                if (BrowserContextPatch.errorMessagesList.GetValueOrDefault(TestContext.CurrentContext.Test.FullName, new List<string>()).Count > 0)
                    MarkSessionStatus("failed", JsonConvert.SerializeObject(String.Join(", ", BrowserContextPatch.errorMessagesList.GetValueOrDefault(TestContext.CurrentContext.Test.FullName, new List<string>()))), page);
                else
                    // Final session marking.
                    MarkSessionStatus("passed", "Passed", page);
            }
            return true;
        }
        catch { }
        return true;
    }
}

[HarmonyPatch]
[HarmonyPatch(typeof(WorkerAwareTest), nameof(WorkerAwareTest.TestOk))]
class TestOKPatch
{
    public static bool Prefix(ref Boolean __result)
    {
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(NUnit.Framework.Internal.TestResult), nameof(NUnit.Framework.Internal.TestResult.SetResult))]
[HarmonyPatch(new Type[] { typeof(NUnit.Framework.Interfaces.ResultState), typeof(string), typeof(string) })]
class Patch07
{

    public static void annotateSession(string data, string level, IPage page)
    {
        JObject executorObject = new JObject();
        JObject argumentsObject = new JObject
        {
            { "data", data },
            { "level", level }
        };
        executorObject.Add("action", "annotate");
        executorObject.Add("arguments", argumentsObject);
        _ = page.EvaluateAsync("_ => {}", "browserstack_executor: " + executorObject.ToString()).Result;
    }

    static bool Prefix(ref string stackTrace, string message, NUnit.Framework.Interfaces.ResultState resultState)
    {
        if (stackTrace != null)
        stackTrace = Regex.Replace(stackTrace, @"(_Patch)(\d+)(?!.*\d)", "");

        try
        {
            var page = BrowserContextPatch.pages__.GetValueOrDefault(TestContext.CurrentContext.Test.FullName, null);
            string storedTestName = TestContext.CurrentContext.Test.FullName;
            if (page == null)
            {
                foreach (var testName in BrowserContextPatch.pages__)
                {
                    if (!TestContext.CurrentContext.Test.FullName.Equals(testName.Key) && TestContext.CurrentContext.Test.FullName.Contains(testName.Key))
                    {
                        page = testName.Value;
                        storedTestName = testName.Key;
                    }
                }
            }
            if (page != null && !page.IsClosed)
            {
                if (resultState.Status == NUnit.Framework.Interfaces.TestStatus.Failed)
                {
                    if (BrowserContextPatch.errorMessagesList.ContainsKey(TestContext.CurrentContext.Test.FullName) || BrowserContextPatch.errorMessagesList.ContainsKey(storedTestName))
                    {
                        BrowserContextPatch.errorMessagesList[storedTestName].Add(message);
                    }
                    else
                    {
                        BrowserContextPatch.errorMessagesList.Add(storedTestName, new List<string> { message });
                    }
                    annotateSession(JsonConvert.SerializeObject("Failed - " + TestContext.CurrentContext.Test.FullName + " " + message), "error", page);
                }
                else
                {
                    annotateSession("Passed", "info", page);
                }
            }
        } catch {}
        return true;
    }
}


class PatchTest
{
    public static void Prefix()
    {
    }

    public static void MarkSessionName(string sessionName, IPage page)
    {
        JObject executorObject = new JObject();
        JObject argumentsObject = new JObject
        {
            { "name", sessionName }
        };
        executorObject.Add("action", "setSessionName");
        executorObject.Add("arguments", argumentsObject);
        _ = page.EvaluateAsync("_ => {}", "browserstack_executor: " + executorObject.ToString()).Result;
    }

    public static Exception Finalizer(Exception __exception)
    {
        try {
            var page = BrowserContextPatch.pages__.GetValueOrDefault(TestContext.CurrentContext.Test.FullName, null);
            if (page == null) {
                foreach (var testName in BrowserContextPatch.pages__)
                {
                    if (TestContext.CurrentContext.Test.FullName.Contains(testName.Key))
                    {
                        page = testName.Value;
                    }
                }
            }

            if (page != null && !page.IsClosed)
            {
                var sessionName = TestContext.CurrentContext.Test.FullName;
var status = TestContext.CurrentContext.Result.Outcome.Status;
var message = TestContext.CurrentContext.Result.Message;
 
                if (message != null) message = message.ToString();
                if (__exception != null)
                {
                    status = NUnit.Framework.Interfaces.TestStatus.Failed;
                    message = __exception.Message;
                }

                if (Environment.GetEnvironmentVariable("BROWSERSTACK_SKIP_SESSION_NAME").ToLower() != "true")
                    MarkSessionName(JsonConvert.SerializeObject(sessionName), page);

                
            }
        } catch {}

        return __exception;
    }
}

#pragma warning restore
