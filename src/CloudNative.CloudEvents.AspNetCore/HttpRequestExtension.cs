﻿// Copyright (c) Cloud Native Foundation.
// Licensed under the Apache 2.0 license.
// See LICENSE file in the project root for full license information.

using CloudNative.CloudEvents.Http;
using Microsoft.AspNetCore.Http;
using System;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;

namespace CloudNative.CloudEvents
{
    public static class HttpRequestExtension
    {
        /// <summary>
        /// Converts this HTTP request into a CloudEvent object.
        /// </summary>
        /// <param name="httpRequest">The HTTP request to decode. Must not be null.</param>
        /// <param name="formatter">The event formatter to use to process the request body. Must not be null.</param>
        /// <param name="extensions">The extension attributes to use when populating the CloudEvent. May be null.</param>
        /// <returns>The decoded CloudEvent.</returns>
        /// <exception cref="ArgumentException">The request does not contain a CloudEvent.</exception>
        public static async ValueTask<CloudEvent> ReadCloudEventAsync(this HttpRequest httpRequest,
            CloudEventFormatter formatter,
            params CloudEventAttribute[] extensionAttributes)
        {
            if (HasCloudEventsContentType(httpRequest))
            {
                // TODO: Handle formatter being null
                return await formatter.DecodeStructuredModeMessageAsync(httpRequest.Body, MimeUtilities.CreateContentTypeOrNull(httpRequest.ContentType), extensionAttributes).ConfigureAwait(false);
            }
            else
            {
                var headers = httpRequest.Headers;
                if (!headers.TryGetValue(HttpUtilities.SpecVersionHttpHeader, out var versionId))
                {
                    throw new ArgumentException("Request is not a CloudEvent");
                }
                var version = CloudEventsSpecVersion.FromVersionId(versionId.First());
                if (version is null)
                {
                    throw new ArgumentException($"Unsupported CloudEvents spec version '{versionId.First()}'");
                }

                var cloudEvent = new CloudEvent(version, extensionAttributes);
                foreach (var header in headers)
                {
                    string attributeName = HttpUtilities.GetAttributeNameFromHeaderName(header.Key);
                    if (attributeName is null || attributeName == CloudEventsSpecVersion.SpecVersionAttribute.Name)
                    {
                        continue;
                    }
                    string attributeValue = HttpUtilities.DecodeHeaderValue(header.Value.First());

                    cloudEvent.SetAttributeFromString(attributeName, attributeValue);
                }

                cloudEvent.DataContentType = httpRequest.ContentType;
                if (httpRequest.Body is Stream body)
                {
                    // TODO: This is a bit ugly. We have code in BinaryDataUtilities to handle this, but
                    // we'd rather not expose it...
                    var memoryStream = new MemoryStream();
                    await body.CopyToAsync(memoryStream).ConfigureAwait(false);
                    formatter.DecodeBinaryModeEventData(memoryStream.ToArray(), cloudEvent);
                }
                return cloudEvent;
            }
        }

        private static bool HasCloudEventsContentType(HttpRequest request) =>
            request?.ContentType is var contentType &&
            contentType.StartsWith(CloudEvent.MediaType, StringComparison.InvariantCultureIgnoreCase);
    }
}
