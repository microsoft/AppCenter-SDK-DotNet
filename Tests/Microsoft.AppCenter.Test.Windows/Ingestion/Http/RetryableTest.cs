﻿using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AppCenter.Ingestion;
using Microsoft.AppCenter.Ingestion.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Microsoft.AppCenter.Test.Ingestion.Http
{
    [TestClass]
    public class RetryableTest : IngestionTest
    {
        private static readonly TimeSpan[] Intervals =
        {
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1) 
        };
        private IIngestion _retryableIngestion;

        [TestInitialize]
        public void InitializeRetryableTest()
        {
            _adapter = new Mock<IHttpNetworkAdapter>();
            _retryableIngestion = new RetryableIngestion(new HttpIngestion(_adapter.Object), Intervals);
        }

        /// <summary>
        /// Verify behaviour without exceptions.
        /// </summary>
        [TestMethod]
        public async Task RetryableIngestionSuccess()
        {
            SetupAdapterSendResponse(HttpStatusCode.OK);
            using (var call = _retryableIngestion.Call(AppSecret, InstallId, Logs))
            {
                await call.ToTask();
                VerifyAdapterSend(Times.Once());
            }

            // No throw any exception
        }

        /// <summary>
        /// Verify that retrying on recoverable exceptions.
        /// </summary>
        [TestMethod]
        public async Task RetryableIngestionRepeat1()
        {
            // RequestTimeout - retryable
            SetupAdapterSendResponse(HttpStatusCode.RequestTimeout, HttpStatusCode.OK);
            using (var call = _retryableIngestion.Call(AppSecret, InstallId, Logs))
            {
                await Task.Delay(500);
                Assert.IsFalse(call.IsCompleted);
                VerifyAdapterSend(Times.Exactly(1));

                await Task.Delay(1000);
                Assert.IsTrue(call.IsCompleted);
                VerifyAdapterSend(Times.Exactly(2));
            }
        }

        /// <summary>
        /// Verify that retrying on recoverable exceptions.
        /// </summary>
        [TestMethod]
        public async Task RetryableIngestionRepeat3()
        {
            // RequestTimeout - retryable
            SetupAdapterSendResponse(HttpStatusCode.RequestTimeout, HttpStatusCode.RequestTimeout, HttpStatusCode.RequestTimeout, HttpStatusCode.OK);
            using (var call = _retryableIngestion.Call(AppSecret, InstallId, Logs))
            {
                await Task.Delay(500);
                Assert.IsFalse(call.IsCompleted);
                VerifyAdapterSend(Times.Exactly(1));

                await Task.Delay(1000);
                Assert.IsFalse(call.IsCompleted);
                VerifyAdapterSend(Times.Exactly(2));

                await Task.Delay(1000);
                Assert.IsFalse(call.IsCompleted);
                VerifyAdapterSend(Times.Exactly(3));

                await Task.Delay(1000);
                Assert.IsTrue(call.IsCompleted);
                VerifyAdapterSend(Times.Exactly(4));
            }
        }

        /// <summary>
        /// Verify service call canceling.
        /// </summary>
        [TestMethod]
        public async Task RetryableIngestionCancel()
        {
            // RequestTimeout - retryable
            SetupAdapterSendResponse(HttpStatusCode.RequestTimeout);
            using (var call = _retryableIngestion.Call(AppSecret, InstallId, Logs))
            {
                await Task.Delay(500);
                Assert.IsFalse(call.IsCompleted);
                VerifyAdapterSend(Times.Exactly(1));

                await Task.Delay(1000);
                Assert.IsFalse(call.IsCompleted);
                VerifyAdapterSend(Times.Exactly(2));

                call.Cancel();

                await Task.Delay(1000);
                Assert.IsFalse(call.IsCompleted);
                VerifyAdapterSend(Times.Exactly(2));
            }
        }

        /// <summary>
        /// Verify that not retrying not recoverable exceptions.
        /// </summary>
        [TestMethod]
        public async Task RetryableIngestionException()
        {
            SetupAdapterSendResponse(HttpStatusCode.BadRequest);
            using (var call = _retryableIngestion.Call(AppSecret, InstallId, Logs))
            {
                await Assert.ThrowsExceptionAsync<HttpIngestionException>(() => call.ToTask());
                VerifyAdapterSend(Times.Once());
            }
        }

        /// <summary>
        /// Validate that constructor throws correct exception type with nullable timespan array
        /// </summary>
        [TestMethod]
        public void RetryableIngestionWithNullIntervals()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new RetryableIngestion(new HttpIngestion(_adapter.Object), null));
        }
    }
}
