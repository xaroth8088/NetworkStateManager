using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace NSM.Tests
{
    /// <summary>Preserve native NUnit results across the domain reload at the end of PlayMode.</summary>
    [InitializeOnLoad]
    internal sealed class VerificationResults : ICallbacks
    {
        private static string Output => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "codex-tests");

        static VerificationResults() => TestRunnerApi.RegisterTestCallback(new VerificationResults());
        public void RunStarted(ITestAdaptor testsToRun)
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "latest-failures.txt"), "");
        }
        public void RunFinished(ITestResultAdaptor result) => TestRunnerApi.SaveResultToFile(result, Path.Combine(Output, "latest-results.xml"));
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result)
        {
            if (result.Test.IsSuite || result.TestStatus != TestStatus.Failed) return;
            File.AppendAllText(Path.Combine(Output, "latest-failures.txt"), result.FullName + "\n" + result.Message + "\n" + result.StackTrace + "\n\n");
        }
    }
}
