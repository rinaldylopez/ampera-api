using AuditWorkflow.Data;

using AuditWorkflow.Extensions;

using AuditWorkflow.Helpers;

using AuditWorkflow.Models.Core;

using AuditWorkflow.Models.Global;

using AuditWorkflow.Services;

using Microsoft.AspNetCore.Authorization;

using Microsoft.AspNetCore.Mvc;

using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;



namespace AuditWorkflow.Controllers.Core;



[Authorize]

[Route("api/requests/{requestId:int}/reminders")]

[ApiController]

public class RequestRemindersController(

    AppDbContext context,

    IProfileResourcePermissionService resourcePermissions) : ControllerBase

{

    private readonly AppDbContext _context = context;

    private readonly IProfileResourcePermissionService _resourcePermissions = resourcePermissions;



    [HttpGet]

    public async Task<ActionResult<IEnumerable<RequestReminder>>> GetReminders(

        int requestId,

        CancellationToken cancellationToken)

    {

        var firmId = User.GetFirmId();

        var userId = User.GetUserId();

        if (firmId is null || userId is null) return Unauthorized();

        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))

            return Forbid();

        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, requestId, firmId.Value))

            return NotFound();



        return await _context.RequestReminders

            .AsNoTracking()

            .Include(r => r.CreatedBy)

            .Where(r => r.RequestId == requestId)

            .OrderByDescending(r => r.ReminderDueDate)

            .ThenByDescending(r => r.CreatedAtUtc)

            .ToListAsync(cancellationToken);

    }



    [HttpPost]

    public async Task<ActionResult<RequestReminder>> PostReminder(

        int requestId,

        [FromBody] RequestReminderInput input,

        CancellationToken cancellationToken)

    {

        var firmId = User.GetFirmId();

        var userId = User.GetUserId();

        if (firmId is null || userId is null) return Unauthorized();

        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))

            return Forbid();

        if (!await _resourcePermissions.AllowsFirmMutationAsync(User, ProfileProtectedResource.Requests,

                ResourcePermissionAction.Update))

            return StatusCode(StatusCodes.Status403Forbidden,

                new { message = "No tiene permiso para esta operación." });

        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, requestId, firmId.Value))

            return NotFound();

        if (await TenantQueries.RequestIsClosedAsync(_context, requestId))

            return BadRequest("La solicitud está cerrada.");



        var validation = ValidateInput(input);

        if (validation != null)

            return BadRequest(validation);



        var reminderDueDate = ParseReminderDueDate(input.ReminderDueDate);

        if (reminderDueDate == null)

            return BadRequest("La fecha de vencimiento del recordatorio es obligatoria.");



        var entity = new RequestReminder

        {

            RequestId = requestId,

            ReminderDueDate = reminderDueDate.Value,

            DaysBeforeDue = input.DaysBeforeDue,

            NotifyFirmStaff = input.NotifyFirmStaff,

            NotifyClientPortal = input.NotifyClientPortal,

            IsActive = true,

            Note = input.Note.Trim(),

            CreatedByUserId = userId.Value,

            CreatedAtUtc = ServerClock.Now

        };



        _context.RequestReminders.Add(entity);

        RequestHistoryLog.Add(

            _context,

            requestId,

            userId.Value,

            RequestHistoryLog.Flow,

            $"Recordatorio configurado: vence {reminderDueDate.Value:dd/MM/yyyy}, notificar desde {input.DaysBeforeDue} días antes.");



        await _context.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetReminders), new { requestId }, entity);

    }



    [HttpPut("{reminderId:int}")]

    public async Task<IActionResult> PutReminder(

        int requestId,

        int reminderId,

        [FromBody] RequestReminderInput input,

        CancellationToken cancellationToken)

    {

        var firmId = User.GetFirmId();

        var userId = User.GetUserId();

        if (firmId is null || userId is null) return Unauthorized();

        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))

            return Forbid();

        if (!await _resourcePermissions.AllowsFirmMutationAsync(User, ProfileProtectedResource.Requests,

                ResourcePermissionAction.Update))

            return StatusCode(StatusCodes.Status403Forbidden,

                new { message = "No tiene permiso para esta operación." });

        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, requestId, firmId.Value))

            return NotFound();



        var validation = ValidateInput(input);

        if (validation != null)

            return BadRequest(validation);



        var reminderDueDate = ParseReminderDueDate(input.ReminderDueDate);

        if (reminderDueDate == null)

            return BadRequest("La fecha de vencimiento del recordatorio es obligatoria.");



        var entity = await _context.RequestReminders

            .FirstOrDefaultAsync(r => r.RequestReminderId == reminderId && r.RequestId == requestId, cancellationToken);

        if (entity == null)

            return NotFound();



        entity.ReminderDueDate = reminderDueDate.Value;

        entity.DaysBeforeDue = input.DaysBeforeDue;

        entity.NotifyFirmStaff = input.NotifyFirmStaff;

        entity.NotifyClientPortal = input.NotifyClientPortal;

        entity.IsActive = input.IsActive;

        entity.Note = input.Note.Trim();



        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();

    }



    [HttpDelete("{reminderId:int}")]

    public async Task<IActionResult> DeleteReminder(

        int requestId,

        int reminderId,

        CancellationToken cancellationToken)

    {

        var firmId = User.GetFirmId();

        var userId = User.GetUserId();

        if (firmId is null || userId is null) return Unauthorized();

        if (await TenantQueries.UserIsClientPortalAsync(_context, userId.Value))

            return Forbid();

        if (!await _resourcePermissions.AllowsFirmMutationAsync(User, ProfileProtectedResource.Requests,

                ResourcePermissionAction.Update))

            return StatusCode(StatusCodes.Status403Forbidden,

                new { message = "No tiene permiso para esta operación." });

        if (!await TenantQueries.RequestBelongsToFirmAsync(_context, requestId, firmId.Value))

            return NotFound();



        var entity = await _context.RequestReminders

            .FirstOrDefaultAsync(r => r.RequestReminderId == reminderId && r.RequestId == requestId, cancellationToken);

        if (entity == null)

            return NotFound();



        _context.RequestReminders.Remove(entity);

        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();

    }



    public class RequestReminderInput

    {

        [Required]

        public DateTime ReminderDueDate { get; set; }



        [Range(1, 365)]

        public int DaysBeforeDue { get; set; }



        public bool NotifyFirmStaff { get; set; } = true;



        public bool NotifyClientPortal { get; set; } = true;



        public bool IsActive { get; set; } = true;



        [Required]
        [MaxLength(500)]
        public string? Note { get; set; }

    }



    private static DateTime? ParseReminderDueDate(DateTime raw)

    {

        if (raw == default) return null;

        return DateTime.SpecifyKind(raw.Date, DateTimeKind.Utc);

    }



    private static string? ValidateInput(RequestReminderInput input)

    {

        if (input.ReminderDueDate == default)

            return "La fecha de vencimiento del recordatorio es obligatoria.";

        if (input.DaysBeforeDue < 1 || input.DaysBeforeDue > 365)

            return "Los días antes del vencimiento deben estar entre 1 y 365.";

        if (!input.NotifyFirmStaff && !input.NotifyClientPortal)
            return "Seleccione al menos un destinatario (firma o portal cliente).";
        if (string.IsNullOrWhiteSpace(input.Note))
            return "La nota es obligatoria.";
        if (input.Note.Trim().Length > 500)
            return "La nota no puede superar 500 caracteres.";

        return null;

    }

}


