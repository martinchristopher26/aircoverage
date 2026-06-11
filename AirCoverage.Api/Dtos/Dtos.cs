namespace AirCoverage.Api.Dtos;

public record LoginRequest(string Username, string Password);
public record UserResponse(string Username, string DisplayName);
