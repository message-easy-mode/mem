namespace HostAgent.Runtime.Ingress;


public interface IRoutePolicyResolver
{
    RoutePublishRequest ApplyDefaults(RoutePublishRequest request);
}
