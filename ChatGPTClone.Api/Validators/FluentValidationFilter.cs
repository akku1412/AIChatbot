using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ChatGPTClone.Api.Validators;

/// <summary>
/// Runs the registered FluentValidation validator for every action argument and
/// short-circuits with a 400 ValidationProblemDetails when any rule fails.
/// </summary>
public sealed class FluentValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(
                new ValidationContext<object>(argument), context.HttpContext.RequestAborted);

            foreach (var error in result.Errors)
            {
                context.ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            }
        }

        if (!context.ModelState.IsValid)
        {
            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(context.ModelState)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Please correct the highlighted fields",
            });
            return;
        }

        await next();
    }
}
