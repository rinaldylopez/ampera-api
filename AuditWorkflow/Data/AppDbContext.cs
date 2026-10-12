using AuditWorkflow.Models.Auth;
using AuditWorkflow.Models.Core;
using AuditWorkflow.Models.Global;
using Microsoft.EntityFrameworkCore;

namespace AuditWorkflow.Data
{
    public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {

        /// <summary>
        /// Tablas Maestras y de Seguridad
        /// </summary>
        public DbSet<JobTitle> JobTitle { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<UserFirmMembership> UserFirmMemberships { get; set; }
        public DbSet<UserCustomerMembership> UserCustomerMemberships { get; set; }
        public DbSet<UserRefreshToken> UserRefreshTokens { get; set; }
        public DbSet<UserAccess> UserAccess { get; set; }
        public DbSet<LoginEmailChallenge> LoginEmailChallenges { get; set; }
        public DbSet<Module> Modules { get; set; }
        public DbSet<NavigationItem> NavigationItems { get; set; }
        public DbSet<Profile> Profiles { get; set; }
        public DbSet<ProfileItem> ProfileItems { get; set; }
        public DbSet<ProfileResourcePermission> ProfileResourcePermissions { get; set; }
        public DbSet<Country> Countries { get; set; }
        public DbSet<EconomicActivity> EconomicActivities { get; set; }

        /// <summary>
        /// Core del negocio
        /// </summary>
        public DbSet<Firm> Firms { get; set; }

        // Gesti�n de Clientes y Equipos
        public DbSet<Customer> Customers { get; set; }
        public DbSet<CustomerContact> CustomerContacts { get; set; }
        public DbSet<CustomerContactType> CustomerContactTypes { get; set; }
        public DbSet<Team> Teams { get; set; }
        public DbSet<TeamMember> TeamMembers { get; set; }

        // Gesti�n de Proyectos y Asignaciones
        public DbSet<Project> Projects { get; set; }
        public DbSet<ProjectTeam> ProjectTeams { get; set; }
        public DbSet<ProjectStaff> ProjectStaffs { get; set; }

        public DbSet<ProjectType> ProjectTypes { get; set; }

        // Gesti�n de Solicitudes (PBC)
        public DbSet<Request> Requests { get; set; }
        public DbSet<RequestAssignment> RequestAssignments { get; set; }
        public DbSet<RequestAttachment> RequestAttachments { get; set; }
        public DbSet<RequestProvidedFile> RequestProvidedFiles { get; set; }
        public DbSet<RequestComment> RequestComments { get; set; }
        public DbSet<RequestHistory> RequestHistory { get; set; }
        public DbSet<RequestPublishApprovalRejection> RequestPublishApprovalRejections { get; set; }
        public DbSet<RequestPublishApprovalEvent> RequestPublishApprovalEvents { get; set; }
        public DbSet<RequestReminder> RequestReminders { get; set; }
        public DbSet<RequestReminderDispatch> RequestReminderDispatches { get; set; }
        public DbSet<RequestOverdueReminderDispatch> RequestOverdueReminderDispatches { get; set; }
        public DbSet<RequestChangeRequest> RequestChangeRequests { get; set; }
        public DbSet<UserNotification> UserNotifications { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configuraci�n de User: Evitar borrado en cascada c�clico entre Firm y Customer
            modelBuilder.Entity<User>()
                .HasOne(u => u.Firm)
                .WithMany()
                .HasForeignKey(u => u.FirmId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configuraci�n de Project: Relaci�n con Manager
            modelBuilder.Entity<Project>()
                .HasOne(p => p.Manager)
                .WithMany()
                .HasForeignKey(p => p.ManagerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Project>()
                .HasOne(p => p.PartnerInCharge)
                .WithMany()
                .HasForeignKey(p => p.PartnerInChargeId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configuraci�n de Team: Relaci�n con Customer opcional
            modelBuilder.Entity<Team>()
                .HasOne(t => t.Customer)
                .WithMany()
                .HasForeignKey(t => t.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);

            // �ndices �nicos para tablas puente (evita duplicados)
            modelBuilder.Entity<TeamMember>()
                .HasIndex(tm => new { tm.TeamId, tm.UserId })
                .IsUnique();

            modelBuilder.Entity<ProjectTeam>()
                .HasIndex(pt => new { pt.ProjectId, pt.TeamId })
                .IsUnique();

            modelBuilder.Entity<ProjectStaff>()
                .HasIndex(ps => new { ps.ProjectId, ps.UserId })
                .IsUnique();

            // Configuraci�n de precisi�n para BudgetedHours
            modelBuilder.Entity<Project>()
                .Property(p => p.BudgetedHours)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Project>()
                .HasOne(p => p.ProjectType)
                .WithMany()
                .HasForeignKey(p => p.ProjectTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ProjectType>()
                .HasIndex(pt => new { pt.FirmId, pt.Code })
                .IsUnique();

            // 1. Filtros Globales para Soft Delete
            // Esto hace que las consultas ignoren autom�ticamente lo "eliminado"
            modelBuilder.Entity<RequestAttachment>().HasQueryFilter(a => !a.IsDeleted);
            modelBuilder.Entity<RequestProvidedFile>().HasQueryFilter(a => !a.IsDeleted);

            // 2. Configuraci�n de Request
            modelBuilder.Entity<Request>()
                .HasIndex(r => r.RequestGuid)
                .IsUnique();

            modelBuilder.Entity<Request>()
                .HasOne(r => r.FirmContact)
                .WithMany()
                .HasForeignKey(r => r.FirmContactId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Request>()
                .HasOne(r => r.ClosedBy)
                .WithMany()
                .HasForeignKey(r => r.ClosedById)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Request>()
                .HasOne(r => r.PublishApprovalRequestedBy)
                .WithMany()
                .HasForeignKey(r => r.PublishApprovalRequestedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<UserNotification>()
                .HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserNotification>()
                .HasOne(n => n.Firm)
                .WithMany()
                .HasForeignKey(n => n.FirmId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserNotification>()
                .HasOne(n => n.Request)
                .WithMany()
                .HasForeignKey(n => n.RequestId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<UserNotification>()
                .HasIndex(n => new { n.UserId, n.CreatedAt });

            // 3. Configuraci�n de RequestAttachment
            modelBuilder.Entity<RequestAttachment>()
                .HasOne(a => a.UploadedBy)
                .WithMany()
                .HasForeignKey(a => a.UploadedById)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RequestProvidedFile>()
                .HasOne(a => a.UploadedBy)
                .WithMany()
                .HasForeignKey(a => a.UploadedById)
                .OnDelete(DeleteBehavior.Restrict);

            // 4. Configuraci�n de RequestHistory (Log de auditor�a)
            modelBuilder.Entity<RequestHistory>()
                .HasOne(h => h.ActionBy)
                .WithMany()
                .HasForeignKey(h => h.ActionById)
                .OnDelete(DeleteBehavior.Restrict);

            // 5. Configuraci�n de Comentarios y Multimedia
            modelBuilder.Entity<RequestComment>()
                .HasOne(c => c.User)
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // 6. �ndices para mejorar rendimiento en b�squedas de archivos y comentarios
            modelBuilder.Entity<RequestAttachment>()
                .HasIndex(a => a.RequestId);

            modelBuilder.Entity<RequestProvidedFile>()
                .HasIndex(a => a.RequestId);

            modelBuilder.Entity<RequestComment>()
                .HasIndex(c => c.RequestId);

            modelBuilder.Entity<RequestPublishApprovalRejection>()
                .HasIndex(r => r.RequestId);

            modelBuilder.Entity<RequestPublishApprovalRejection>()
                .HasOne(r => r.Request)
                .WithMany()
                .HasForeignKey(r => r.RequestId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RequestPublishApprovalRejection>()
                .HasOne(r => r.RejectedBy)
                .WithMany()
                .HasForeignKey(r => r.RejectedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RequestPublishApprovalRejection>()
                .HasOne(r => r.RequestedBy)
                .WithMany()
                .HasForeignKey(r => r.RequestedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<RequestPublishApprovalEvent>()
                .HasIndex(e => e.RequestId);

            modelBuilder.Entity<RequestPublishApprovalEvent>()
                .HasOne(e => e.Request)
                .WithMany()
                .HasForeignKey(e => e.RequestId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RequestPublishApprovalEvent>()
                .HasOne(e => e.Actor)
                .WithMany()
                .HasForeignKey(e => e.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RequestPublishApprovalEvent>()
                .HasOne(e => e.Related)
                .WithMany()
                .HasForeignKey(e => e.RelatedUserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<RequestReminder>()
                .HasIndex(r => r.RequestId);

            modelBuilder.Entity<RequestReminder>()
                .HasOne(r => r.Request)
                .WithMany()
                .HasForeignKey(r => r.RequestId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RequestReminder>()
                .HasOne(r => r.CreatedBy)
                .WithMany()
                .HasForeignKey(r => r.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RequestReminderDispatch>()
                .HasIndex(d => new { d.RequestReminderId, d.NotifyOnDate })
                .IsUnique();

            modelBuilder.Entity<RequestReminderDispatch>()
                .HasOne(d => d.RequestReminder)
                .WithMany(r => r.Dispatches)
                .HasForeignKey(d => d.RequestReminderId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RequestOverdueReminderDispatch>()
                .HasIndex(d => new { d.UserId, d.NotifyOnDate })
                .IsUnique();

            modelBuilder.Entity<RequestOverdueReminderDispatch>()
                .HasOne(d => d.User)
                .WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RequestChangeRequest>()
                .HasIndex(c => c.RequestId);

            modelBuilder.Entity<RequestChangeRequest>()
                .HasIndex(c => new { c.RequestId, c.Status });

            modelBuilder.Entity<RequestChangeRequest>()
                .HasOne(c => c.Request)
                .WithMany()
                .HasForeignKey(c => c.RequestId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RequestChangeRequest>()
                .HasOne(c => c.RequestedBy)
                .WithMany()
                .HasForeignKey(c => c.RequestedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RequestChangeRequest>()
                .HasOne(c => c.ReviewedBy)
                .WithMany()
                .HasForeignKey(c => c.ReviewedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<ProfileItem>()
                .HasIndex(pi => new { pi.ProfileId, pi.NavigationItemId })
                .IsUnique();

            modelBuilder.Entity<ProfileResourcePermission>()
                .HasIndex(p => new { p.ProfileId, p.Resource })
                .IsUnique();

            modelBuilder.Entity<ProfileResourcePermission>()
                .HasOne(p => p.Profile)
                .WithMany()
                .HasForeignKey(p => p.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProfileResourcePermission>()
                .Property(p => p.Resource)
                .HasConversion<byte>();

            modelBuilder.Entity<UserFirmMembership>()
                .HasIndex(m => new { m.UserId, m.FirmId })
                .IsUnique();

            modelBuilder.Entity<UserFirmMembership>()
                .HasOne(m => m.User)
                .WithMany(u => u.UserFirmMemberships)
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserFirmMembership>()
                .HasOne(m => m.Firm)
                .WithMany()
                .HasForeignKey(m => m.FirmId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserCustomerMembership>()
                .HasIndex(m => new { m.UserId, m.CustomerId })
                .IsUnique();

            modelBuilder.Entity<UserCustomerMembership>()
                .HasOne(m => m.User)
                .WithMany(u => u.UserCustomerMemberships)
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserCustomerMembership>()
                .HasOne(m => m.Customer)
                .WithMany()
                .HasForeignKey(m => m.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserRefreshToken>()
                .HasOne<Firm>()
                .WithMany()
                .HasForeignKey(t => t.ActiveFirmId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserRefreshToken>()
                .HasOne<Customer>()
                .WithMany()
                .HasForeignKey(t => t.ActiveCustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<EconomicActivity>()
                .HasIndex(e => e.Code)
                .IsUnique();

            modelBuilder.Entity<Customer>()
                .HasOne(c => c.EconomicActivity)
                .WithMany()
                .HasForeignKey(c => c.EconomicActivityId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Customer>()
                .HasOne(c => c.PhoneCountry)
                .WithMany()
                .HasForeignKey(c => c.PhoneCountryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CustomerContactType>()
                .HasIndex(t => t.Code)
                .IsUnique();

            modelBuilder.Entity<CustomerContactType>().HasData(
                new CustomerContactType { CustomerContactTypeId = 1, Code = "ACCOUNTANT", Name = "Contador", SortOrder = 10, IsActive = true },
                new CustomerContactType { CustomerContactTypeId = 2, Code = "LEGAL_REP", Name = "Representante legal", SortOrder = 20, IsActive = true },
                new CustomerContactType { CustomerContactTypeId = 3, Code = "OTHER", Name = "Otro", SortOrder = 30, IsActive = true });

            modelBuilder.Entity<CustomerContact>()
                .HasOne(c => c.Customer)
                .WithMany(c => c.Contacts)
                .HasForeignKey(c => c.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CustomerContact>()
                .HasOne(c => c.ContactType)
                .WithMany()
                .HasForeignKey(c => c.CustomerContactTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CustomerContact>()
                .HasOne(c => c.PhoneCountry)
                .WithMany()
                .HasForeignKey(c => c.PhoneCountryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<LoginEmailChallenge>()
                .HasIndex(c => c.ChallengeToken)
                .IsUnique();

            modelBuilder.Entity<LoginEmailChallenge>()
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
