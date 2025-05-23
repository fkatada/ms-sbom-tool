// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Sbom.Api.Exceptions;
using Microsoft.Sbom.Common;
using Microsoft.Sbom.Common.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Serilog;

namespace Microsoft.Sbom.Api.Executors.Tests;

[TestClass]
public class DirectoryWalkerTests
{
    private readonly Mock<ILogger> mockLogger = new Mock<ILogger>();
    private readonly Mock<IConfiguration> mockConfiguration = new Mock<IConfiguration>();

    [TestInitialize]
    public void TestInitialize()
    {
        mockConfiguration.Setup(c => c.FollowSymlinks).Returns(new ConfigurationSetting<bool> { Source = SettingSource.Default, Value = true });
    }

    [TestMethod]
    public async Task DirectoryWalkerTests_ValidRoot_SucceedsAsync()
    {
        var files = new HashSet<string>
        {
            @"Test\Sample\NoRead.txt",
            @"Test\Sample\Sample.txt",
        };

        var mockFSUtils = new Mock<IFileSystemUtils>();
        mockFSUtils.Setup(m => m.DirectoryExists(It.IsAny<string>())).Returns(true).Verifiable();
        mockFSUtils.SetupSequence(m => m.GetDirectories(It.IsAny<string>(), true))
            .Returns(new List<string>() { "Sample" })
            .Returns(new List<string>());
        mockFSUtils.Setup(m => m.GetFilesInDirectory(It.Is<string>(d => d == "Sample"), true)).Returns(files).Verifiable();
        mockFSUtils.Setup(m => m.GetFilesInDirectory(It.Is<string>(d => d == "Test"), true)).Returns(new List<string>()).Verifiable();

        var filesChannelReader = new DirectoryWalker(mockFSUtils.Object, mockLogger.Object, mockConfiguration.Object).GetFilesRecursively(@"Test");

        await foreach (var file in filesChannelReader.file.ReadAllAsync())
        {
            Assert.IsTrue(files.Remove(file));
        }

        await foreach (var file in filesChannelReader.file.ReadAllAsync())
        {
            Assert.IsTrue(files.Remove(file));
        }

        await foreach (var error in filesChannelReader.errors.ReadAllAsync())
        {
            Assert.Fail($"Error thrown for {error.Path}: {error.ErrorType}");
        }

        Assert.AreEqual(0, files.Count);
        mockFSUtils.VerifyAll();
    }

    [TestMethod]
    public void DirectoryWalkerTests_DirectoryDoesntExist_Fails()
    {
        var mockFSUtils = new Mock<IFileSystemUtils>();
        mockFSUtils.Setup(m => m.DirectoryExists(It.IsAny<string>())).Returns(false).Verifiable();
        Assert.ThrowsException<InvalidPathException>(() =>
            new DirectoryWalker(mockFSUtils.Object, mockLogger.Object, mockConfiguration.Object).GetFilesRecursively(@"BadDir"));
        mockFSUtils.VerifyAll();
    }

    [TestMethod]
    public async Task DirectoryWalkerTests_UnreachableFile_FailsAsync()
    {
        var files = new HashSet<string>
        {
            @"Test\SampleBadDir\Test.txt"
        };
        var mockFSUtils = new Mock<IFileSystemUtils>();
        mockFSUtils.Setup(m => m.DirectoryExists(It.IsAny<string>())).Returns(true).Verifiable();
        mockFSUtils.SetupSequence(m => m.GetDirectories(It.IsAny<string>(), true))
            .Returns(new List<string>() { "Sample" })
            .Returns(new List<string>() { "Failed" });
        mockFSUtils.Setup(m => m.GetFilesInDirectory(It.Is<string>(d => d == "Sample"), true)).Returns(files).Verifiable();
        mockFSUtils.Setup(m => m.GetFilesInDirectory(It.Is<string>(d => d == "Test"), true)).Returns(new List<string>()).Verifiable();
        mockFSUtils.Setup(m => m.GetFilesInDirectory(It.Is<string>(d => d == "Failed"), true)).Throws(new UnauthorizedAccessException()).Verifiable();

        var filesChannelReader = new DirectoryWalker(mockFSUtils.Object, mockLogger.Object, mockConfiguration.Object).GetFilesRecursively(@"Test");
        var errorCount = 0;

        await foreach (var error in filesChannelReader.errors.ReadAllAsync())
        {
            errorCount++;
        }

        await foreach (var file in filesChannelReader.file.ReadAllAsync())
        {
            Assert.IsTrue(files.Remove(file));
        }

        Assert.AreEqual(1, errorCount);
        Assert.AreEqual(0, files.Count);
        mockFSUtils.VerifyAll();
    }
}
