using AirCoverage.Api.Models;

namespace AirCoverage.Api.Data;

/// <summary>
/// Seeds the sample queue from the prototype the first time the DB is empty.
/// Dates are relative to "now" (like the prototype's daysAgo helper) so the
/// days-open / aging behaviour is meaningful in a fresh environment.
/// </summary>
public static class DbSeeder
{
    public static void Seed(AppDbContext db)
    {
        if (db.Items.Any()) return;

        static DateTime DaysAgo(int n) => DateTime.UtcNow.Date.AddDays(-n).AddHours(9);

        db.Items.AddRange(
            new Item { Number = "AC-1042", Priority = "Critical", Status = "In Progress", RequestedBy = "On-call", Assignee = "Alex Reyes",   TicketType = "ADO",         TicketRef = "#48211", Received = DaysAgo(0),  Updated = DaysAgo(0),
                Title = "Production API returning 500s on document upload",
                Description = "Customers cannot attach files to requests. ~30% of uploads failing since this morning. Storage SDK timeout suspected." },
            new Item { Number = "AC-1041", Priority = "Critical", Status = "New",         RequestedBy = "Support", Assignee = "",             TicketType = "ConnectWise", TicketRef = "#88204", Received = DaysAgo(1),  Updated = null,
                Title = "Customer cannot log in after SSO config change",
                Description = "Acme Co reports redirect loop on login following yesterday's identity provider update. Blocking ~40 users." },
            new Item { Number = "AC-1038", Priority = "High",     Status = "In Progress", RequestedBy = "On-call", Assignee = "Marcus Tran",  TicketType = "ADO",         TicketRef = "#48190", Received = DaysAgo(2),  Updated = DaysAgo(1),
                Title = "Memory leak in background worker",
                Description = "Worker process RSS climbs steadily and OOMs about every 6 hours; restarted manually for now." },
            new Item { Number = "AC-1035", Priority = "High",     Status = "Waiting",     RequestedBy = "Support", Assignee = "Priya Shah",   TicketType = "ADO",         TicketRef = "#48155", Received = DaysAgo(4),  Updated = DaysAgo(2),
                Title = "Nightly export job failing intermittently",
                Description = "Scheduled CSV export to the county SFTP fails ~1 in 3 nights with a connection reset. Need retry + alerting." },
            new Item { Number = "AC-1030", Priority = "High",     Status = "New",         RequestedBy = "Customer", Assignee = "",            TicketType = null,          TicketRef = null,     Received = DaysAgo(5),  Updated = null,
                Title = "Slow query on Requests dashboard",
                Description = "Dashboard taking 8-12s to load for tenants with >5k requests. Missing index on status + date likely." },
            new Item { Number = "AC-1027", Priority = "Medium",   Status = "Waiting",     RequestedBy = "Support", Assignee = "Dana Kim",     TicketType = "ConnectWise", TicketRef = "#88150", Received = DaysAgo(8),  Updated = DaysAgo(3),
                Title = "Investigate duplicate notification emails",
                Description = "A handful of users got the same assignment email 2-3 times. Possibly a webhook re-delivery without idempotency." },
            new Item { Number = "AC-1024", Priority = "Medium",   Status = "New",         RequestedBy = "Eng",     Assignee = "",             TicketType = "ADO",         TicketRef = "#48099", Received = DaysAgo(11), Updated = null,
                Title = "Add retry logic to outbound email service",
                Description = "Transient SMTP failures currently drop the message silently. Add bounded retry + dead-letter log." },
            new Item { Number = "AC-1019", Priority = "Medium",   Status = "New",         RequestedBy = "Eng",     Assignee = "Sam Whitfield", TicketType = null,         TicketRef = null,     Received = DaysAgo(15), Updated = DaysAgo(6),
                Title = "Renew expired SSL cert on staging",
                Description = "staging.justfoia cert expires next week; renew and automate going forward." },
            new Item { Number = "AC-1011", Priority = "Low",      Status = "New",         RequestedBy = "Support", Assignee = "",             TicketType = null,          TicketRef = null,     Received = DaysAgo(19), Updated = null,
                Title = "Typo in request confirmation email template",
                Description = "\"Recieved\" should be \"Received\" in the auto-confirmation. Minor but customer-facing." },
            new Item { Number = "AC-1004", Priority = "Critical", Status = "Resolved",    RequestedBy = "On-call", Assignee = "Alex Reyes",   TicketType = "ADO",         TicketRef = "#47980", Received = DaysAgo(22), Updated = DaysAgo(1),
                Title = "Refresh token rotation edge case",
                Description = "Token refresh occasionally 401s when two tabs refresh simultaneously. Fix applied, verifying in prod." },
            new Item { Number = "AC-0998", Priority = "Low",      Status = "Closed",      RequestedBy = "Eng",     Assignee = "Marcus Tran",  TicketType = "ADO",         TicketRef = "#47901", Received = DaysAgo(30), Updated = DaysAgo(7),
                Title = "Deprecate legacy /v1 report endpoint",
                Description = "No traffic in 90 days. Removed and redirected; closing out." }
        );

        db.SaveChanges();
    }
}
