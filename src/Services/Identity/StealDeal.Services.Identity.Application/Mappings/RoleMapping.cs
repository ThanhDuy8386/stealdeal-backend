using StealDeal.Services.Identity.Application.DTOs.Responses;
using StealDeal.Services.Identity.Domain.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace StealDeal.Services.Identity.Application.Mappings
{
    public static class RoleMapping
    {
        public static RoleResponse ToRoleResponse (this Role role)
        {
            return new RoleResponse
            {
                Id = role.Id,
                Name = role.Name
            };
        }
    }
}
