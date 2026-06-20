using Aedis.App1.Api.Dtos.Requests;
using FluentValidation;

namespace Aedis.App1.Api.Validators;

/// <summary>
///     Validação de entrada da criação de produto. Falhas viram <c>422 Unprocessable Entity</c> no formato
///     ProblemDetails (campo <c>errors</c> por propriedade), antes de o comando chegar ao handler.
/// </summary>
public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest> {
    /// <summary>
    ///     Define as regras de validação.
    /// </summary>
    public CreateProductRequestValidator() {
        RuleFor(request => request.Code).NotEmpty().MaximumLength(50);
        RuleFor(request => request.Name).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Price).GreaterThanOrEqualTo(0);
    }
}
