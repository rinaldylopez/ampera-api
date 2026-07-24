using System.ComponentModel.DataAnnotations;

namespace AuditWorkflow.Models.Auth
{
    public class UserAccess
    {
        [Key]
        public int UserAccesId { get; set; }
        public int UserId { get; set; }
        public DateTime DateAcces { get; set; }
        public string MachineName { get; set; }
        public string IpAddress { get; set; }
    }
}
