using System.Net.Sockets;
using System.Security.Authentication;

namespace Earlycare.CloudMigration;

public static class DeploymentErrorClassifier
{
    public static string Classify(Exception exception, string stage, int? mysqlNumber)
    {
        if (exception is DeploymentException safe) return safe.Code;
        if (mysqlNumber is 1045 or 1698) return "DATABASE_AUTH_FAILED";
        if (mysqlNumber is 1044 or 1142 or 1143 or 1227) return "DATABASE_PERMISSION_DENIED";
        if (mysqlNumber == 1049) return "DATABASE_NOT_FOUND";
        if (mysqlNumber is 2026 or 2059 or 2061) return "DATABASE_SECURITY_NEGOTIATION_FAILED";
        // Inspect typed inner causes only; never inspect messages.
        for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
            if (cause is AuthenticationException) return "DATABASE_SECURITY_NEGOTIATION_FAILED";
        if (stage is "OPEN_CONNECTION" or "QUERY_SERVER_INFO" or "QUERY_METADATA")
        {
            if (mysqlNumber is 1042 or 2002 or 2003 or 2006 or 2013) return "DATABASE_CONNECTION_FAILED";
            for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
                if (cause is SocketException or TimeoutException) return "DATABASE_CONNECTION_FAILED";
        }
        return "INTERNAL_ERROR";
    }
}
