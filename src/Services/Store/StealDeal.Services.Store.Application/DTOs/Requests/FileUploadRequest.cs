using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Store.Application.DTOs.Requests
{
    public record FileUploadRequest(
        Stream Stream,
        string FileName,
        string ContentType,
        long Length
    );
}
