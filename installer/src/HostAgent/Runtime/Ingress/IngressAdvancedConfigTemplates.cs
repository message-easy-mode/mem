namespace HostAgent.Runtime.Ingress;

public static class IngressAdvancedConfigTemplates
{
    public static string MatrixWellKnown() => """
location = /.well-known/matrix/client {
    default_type application/json;
    add_header Access-Control-Allow-Origin *;
    return 200 "{\"m.homeserver\":{\"base_url\":\"https://$host\"}}";
}

location = /.well-known/matrix/server {
    default_type application/json;
    return 200 "{\"m.server\":\"$host:443\"}";
}
""";


    public static string MatrixLocalOnly() => """
location = /.well-known/matrix/client {
    default_type application/json;
    add_header Access-Control-Allow-Origin *;
    return 200 "{\"m.homeserver\":{\"base_url\":\"https://$host\"}}";
}

location = /.well-known/matrix/server {
    return 404;
}

location ^~ /_matrix/federation/ {
    return 404;
}

location ^~ /_matrix/key/ {
    return 404;
}
""";
}
