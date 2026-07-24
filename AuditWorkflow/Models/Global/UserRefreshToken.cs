using System;
using System.ComponentModel.DataAnnotations;

namespace AuditWorkflow.Models.Global
{
    public class UserRefreshToken
    {
        [Key]
        public int TokenId { get; set; }
        public int UserId { get; set; }

        /// <summary>Firma activa de la sesión (debe coincidir con el claim JWT FirmId).</summary>
        public int ActiveFirmId { get; set; }

        /// <summary>Solo usuarios portal cliente: cliente activo (claim JWT CustomerId).</summary>
        public int? ActiveCustomerId { get; set; }

        public string Token { get; set; }
        public string JwtId { get; set; }
        public bool IsUsed { get; set; }
        public bool IsRevoked { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
