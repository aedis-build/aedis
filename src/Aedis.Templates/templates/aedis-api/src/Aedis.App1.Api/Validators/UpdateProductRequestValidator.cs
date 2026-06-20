using Aedis.App1.Api.Dtos.Requests;
using FluentValidation;

namespace Aedis.App1.Api.Validators;

/// <summary>
///     Validação de entrada da atualização de produto. Falhas viram <c>422 Unprocessable Entity</c>.
/// </summary>
public sealed class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest> {
    /// <summary>
    ///     Define as regras de validação.
    /// </summary>
    public UpdateProductRequestValidator() {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Price).GreaterThanOrEqualTo(0);
    }
}
