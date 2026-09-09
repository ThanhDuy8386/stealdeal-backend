using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Infrastructure.Configuration
{
    public class S3Settings
    {
        public string Region { get; set; } = null!;
        public string PublicBucketName { get; set; } = null!;
        public string AccessKeyId { get; set; } = null!;
        public string SecretAccessKey { get; set; } = null!;
    }
}
