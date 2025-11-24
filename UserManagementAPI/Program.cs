using System.Text.Json;
using UserManagementAPI.Models;
using System.IdentityModel.Tokens.Jwt;   // <-- necesario para JwtSecurityTokenHandler
using Microsoft.IdentityModel.Tokens;   // <-- necesario para TokenValidationParameters y SymmetricSecurityKey
using System.Text; 

var builder = WebApplication.CreateBuilder(args);

var users = new List<User>
{
    new User(1, "Alice Johnson", "alice@techhive.com"),
    new User(2, "Bob Smith", "bob@techhive.com"),
    new User(3, "Charlie Davis", "charlie@techhive.com")
};

var app = builder.Build();

//=========================
// Error handling global
//=========================
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var errorResponse = new { status = 500, message = "Ha ocurrido un error inesperado. Inténtalo de nuevo más tarde." };
        await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse));
    });
});

//=========================
//Authentication middleware
//=========================
app.Use(async (context, next) =>
{
    var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
    if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { status = 401, message = "Unauthorized: missing or invalid token." }));
        return;
    }

    var token = authHeader.Substring("Bearer ".Length).Trim();
    try
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes("SuperSecretKey12345");

        tokenHandler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = true,
            ValidIssuer = "TechHive",
            ValidateAudience = true,
            ValidAudience = "TechHiveUsers",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        }, out _);

        await next();
    }
    catch
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { status = 401, message = "Unauthorized: invalid token." }));
    }
});

//=========================
//Logging middleware
//=========================
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("➡️ Request: {method} {path}", context.Request.Method, context.Request.Path);

    var originalBodyStream = context.Response.Body;
    using var responseBody = new MemoryStream();
    context.Response.Body = responseBody;

    await next();

    context.Response.Body.Seek(0, SeekOrigin.Begin);
    var text = await new StreamReader(context.Response.Body).ReadToEndAsync();
    context.Response.Body.Seek(0, SeekOrigin.Begin);

    var truncated = text.Length > 500 ? text.Substring(0, 500) + "..." : text;
    logger.LogInformation("⬅️ Response: {statusCode} {body}", context.Response.StatusCode, truncated);

    await responseBody.CopyToAsync(originalBodyStream);
});

app.UseHttpsRedirection();

// =====================
// CRUD Endpoints
// =====================

// GET paginado
app.MapGet("/api/users", (int? page, int? pageSize) =>
{
    try
    {
        int currentPage = page is null or <= 0 ? 1 : page.Value;
        int size = pageSize is null or <= 0 ? 10 : pageSize.Value;

        var total = users.Count;
        var pagedUsers = users
            .OrderBy(u => u.Id)
            .Skip((currentPage - 1) * size)
            .Take(size)
            .ToList();

        return Results.Json(new
        {
            status = 200,
            data = new
            {
                page = currentPage,
                pageSize = size,
                totalItems = total,
                totalPages = (int)Math.Ceiling(total / (double)size),
                items = pagedUsers
            }
        }, statusCode: 200);
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = 500, message = $"Error retrieving users: {ex.Message}" }, statusCode: 500);
    }
});

// GET by ID
app.MapGet("/api/users/{id}", (int id) =>
{
    try
    {
        var user = users.FirstOrDefault(u => u.Id == id);
        if (user is null)
            return Results.Json(new { status = 404, message = $"User with Id {id} not found." }, statusCode: 404);

        return Results.Json(new { status = 200, data = user }, statusCode: 200);
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = 500, message = $"Error retrieving user: {ex.Message}" }, statusCode: 500);
    }
});

// POST new user
app.MapPost("/api/users", (UserCreateRequest request) =>
{
    try
    {
        var validationError = UserValidator.ValidateUser(request.Name, request.Email);
        if (validationError is not null)
            return Results.Json(new { status = 400, message = validationError }, statusCode: 400);

        var nextId = users.Any() ? users.Max(u => u.Id) + 1 : 1;
        var user = new User(nextId, request.Name.Trim(), request.Email.Trim());

        users.Add(user);
        return Results.Json(new { status = 201, data = user }, statusCode: 201);
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = 500, message = $"Error creating user: {ex.Message}" }, statusCode: 500);
    }
});

// PUT update user
app.MapPut("/api/users/{id}", (int id, UserUpdateRequest request) =>
{
    try
    {
        var existing = users.FirstOrDefault(u => u.Id == id);
        if (existing is null)
            return Results.Json(new { status = 404, message = $"User with Id {id} not found." }, statusCode: 404);

        var validationError = UserValidator.ValidateUser(request.Name, request.Email);
        if (validationError is not null)
            return Results.Json(new { status = 400, message = validationError }, statusCode: 400);

        var updated = new User(id, request.Name.Trim(), request.Email.Trim());
        var index = users.FindIndex(u => u.Id == id);
        users[index] = updated;

        return Results.Json(new { status = 200, data = updated }, statusCode: 200);
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = 500, message = $"Error updating user: {ex.Message}" }, statusCode: 500);
    }
});

// DELETE
app.MapDelete("/api/users/{id}", (int id) =>
{
    try
    {
        var user = users.FirstOrDefault(u => u.Id == id);
        if (user is null)
            return Results.Json(new { status = 404, message = $"User with Id {id} not found." }, statusCode: 404);

        users.Remove(user);
        // ✅ devolvemos 200 con mensaje en lugar de 204 con body
        return Results.Json(new { status = 200, message = "User deleted successfully." }, statusCode: 200);
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = 500, message = $"Error deleting user: {ex.Message}" }, statusCode: 500);
    }
});

app.Run();