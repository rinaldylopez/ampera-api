using System.ComponentModel.DataAnnotations;

namespace AuditWorkflow.Models.Core
{
    public class Firm
    {
        [Key]
        public int FirmId { get; set; }

        [MaxLength(50)]
        public string TaxId { get; set; }

        [MaxLength(250)]
        public string Name { get; set; }

        [MaxLength(250)]
        public string Phone { get; set; }

        [MaxLength(250)]
        public string Email { get; set; }
    }
}
