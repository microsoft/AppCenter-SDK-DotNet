﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using Microsoft.AppCenter.Ingestion.Http;
using Microsoft.AppCenter.Ingestion.Models;
using Moq;

namespace Microsoft.AppCenter.Test.Ingestion.Http
{
    public class IngestionTest
    {
        protected Mock<IHttpNetworkAdapter> _adapter;

        protected string AppSecret => Guid.NewGuid().ToString();
        protected Guid InstallId => Guid.NewGuid();
        protected IList<Log> Logs => new List<Log>();

        /// <summary>
        /// Helper for setup responce.
        /// </summary>
        protected void SetupAdapterSendResponse(params HttpStatusCode[] statusCodes)
        {
            var setup = _adapter
                .SetupSequence(a => a.SendAsync(
                    It.IsAny<string>(),
                    "POST",
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()));
            foreach (var statusCode in statusCodes)
            {
                setup.Returns(statusCode == HttpStatusCode.OK
                    ? TaskExtension.GetCompletedTask("")
                    : TaskExtension.GetFaultedTask<string>(new HttpIngestionException("")
                    {
                        StatusCode = (int)statusCode
                    }));
            }
        }

        /// <summary>
        /// Helper for verify SendAsync call.
        /// </summary>
        protected void VerifyAdapterSend(Times times)
        {
            _adapter
                .Verify(a => a.SendAsync(
                    It.IsAny<string>(),
                    "POST",
                    It.IsAny<IDictionary<string, string>>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()), times);
        }
    }
}
