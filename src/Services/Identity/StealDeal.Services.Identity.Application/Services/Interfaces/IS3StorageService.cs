using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.Services.Interfaces
{
    public interface IS3StorageService
    {
        Task<string> UploadImageAsync(
            Stream fileStream,
            string originalFileName,
            string contentType,
            long fileSize,
            string folder,
            CancellationToken cancellationToken = default);
    }
}
