using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using StealDeal.Services.Identity.Application.Exceptions;
using StealDeal.Services.Identity.Application.Services.Interfaces;
using StealDeal.Services.Identity.Infrastructure.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Infrastructure.Storage
{
    public class S3StorageService : IS3StorageService
    {
        private const long MaxFileSize = 5 * 1024 * 1024; // 5 MB
        private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/jpg",
            "image/png",
            "image/webp"
        };

        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpeg",
            ".jpg",
            ".png",
            ".webp"
        };

        private readonly S3Settings _settings;
        private readonly IAmazonS3 _s3Client;

        public S3StorageService(IOptions<S3Settings> options)
        {
            _settings = options.Value;

            var credentials = new BasicAWSCredentials(_settings.AccessKeyId, _settings.SecretAccessKey);

            var config = new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(_settings.Region)
            };

            _s3Client = new AmazonS3Client(credentials, config);
        }

        public async Task<string> UploadImageAsync(Stream fileStream, string originalFileName, string contentType, long fileSize, string folder, CancellationToken cancellationToken = default)
        {
            // Validate file size
            if (fileSize <= 0)
                throw new BadRequestException("Uploaded file cannot be empty.");

            if (fileSize > MaxFileSize)
                throw new BadRequestException($"Uploaded file size exceeds the maximum allowed size of {MaxFileSize / (1024 * 1024)} MB.");

            // Validate content type
            if (string.IsNullOrWhiteSpace(contentType) || !AllowedMimeTypes.Contains(contentType))
                throw new BadRequestException("Invalid file type. Only JPEG, PNG, and WebP images are allowed.");

            // Validate file extension
            var extension = Path.GetExtension(originalFileName);
            if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
                throw new BadRequestException("Invalid file extension. Only JPEG, PNG, and WebP images are allowed.");

            // generate random guid filname under organized folder
            var cleanFolder = folder.Trim('/');
            var key = $"{cleanFolder}/{Guid.NewGuid()}{extension.ToLowerInvariant()}";

            // upload to S3
            var request = new PutObjectRequest
            {
                BucketName = _settings.PublicBucketName,
                Key = key,
                InputStream = fileStream,
                ContentType = contentType
            };

            await _s3Client.PutObjectAsync(request, cancellationToken);

            return $"https://{_settings.PublicBucketName}.s3.{_settings.Region}.amazonaws.com/{key}";
        }
    }
}
