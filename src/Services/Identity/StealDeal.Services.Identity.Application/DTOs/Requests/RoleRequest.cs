using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace StealDeal.Services.Identity.Application.DTOs.Requests
{
    public class RoleRequest
    {
        [Required]
        [MaxLength(50)]
        public string Name { get; set; } = null!;
    }
}
