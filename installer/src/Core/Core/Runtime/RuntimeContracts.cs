using System;
using Infrastructure.Docker.Models;

namespace Core.Runtime;

public sealed record RuntimeActionResponse(
    bool Success,
    string Message,
    DockerContainerInspection? Container
);