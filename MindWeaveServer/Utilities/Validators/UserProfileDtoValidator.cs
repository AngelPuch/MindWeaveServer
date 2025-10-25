using FluentValidation;
using MindWeaveServer.Contracts.DataContracts;
using MindWeaveServer.Resources;
using System;
using System.Linq;
using MindWeaveServer.Contracts.DataContracts.Authentication;

namespace MindWeaveServer.Utilities.Validators
{
    public class UserProfileDtoValidator : AbstractValidator<UserProfileDto>
    {
        public UserProfileDtoValidator()
        {
            // Reglas para el Nombre de Usuario
            RuleFor(x => x.username)
                .NotEmpty().WithMessage(Lang.ValidationUsernameRequired)
                .Length(3, 16).WithMessage(Lang.ValidationUsernameLength)
                .Matches("^[a-zA-Z0-9]*$").WithMessage(Lang.ValidationUsernameAlphanumeric); // SOLO LETRAS Y NÚMEROS

            // Reglas para el Email
            RuleFor(x => x.email)
                .NotEmpty().WithMessage(Lang.ValidationEmailRequired)
                .EmailAddress().WithMessage(Lang.ValidationEmailFormat);

            // Reglas para el Nombre y Apellido
            RuleFor(x => x.firstName)
                .NotEmpty().WithMessage(Lang.ValidationFirstNameRequired)
                .MaximumLength(45).WithMessage(Lang.ValidationFirstNameLength)
                .Must(NotHaveLeadingOrTrailingWhitespace).WithMessage(Lang.ValidationNoLeadingOrTrailingWhitespace) // SIN ESPACIOS AL INICIO/FINAL
                .Matches("^[a-zA-ZáéíóúÁÉÍÓÚñÑ ]*$").WithMessage(Lang.ValidationOnlyLetters); // SOLO LETRAS Y ESPACIOS INTERNOS

            RuleFor(x => x.lastName)
                .MaximumLength(45).WithMessage(Lang.ValidationLastNameLength)
                .Must(NotHaveLeadingOrTrailingWhitespace).When(x => !string.IsNullOrEmpty(x.lastName)).WithMessage(Lang.ValidationNoLeadingOrTrailingWhitespace)
                .Matches("^[a-zA-ZáéíóúÁÉÍÓÚñÑ ]*$").When(x => !string.IsNullOrEmpty(x.lastName)).WithMessage(Lang.ValidationOnlyLetters);

            // Regla para la Fecha de Nacimiento
            RuleFor(x => x.dateOfBirth)
                .NotNull().WithMessage(Lang.ValidationDateOfBirthRequired)
                .Must(BeAValidAge).WithMessage(Lang.ValidationDateOfBirthMinimumAge)
                .Must(BeARealisticAge).WithMessage(Lang.ValidationDateOfBirthRealistic); // EDAD REALISTA
        }

        // --- FUNCIONES DE AYUDA PARA VALIDACIÓN ---

        private bool NotHaveLeadingOrTrailingWhitespace(string value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            return value.Trim() == value;
        }

        private bool BeAValidAge(DateTime dateOfBirth)
        {
            return dateOfBirth.Date <= DateTime.Now.Date.AddYears(-13);
        }

        private bool BeARealisticAge(DateTime dateOfBirth)
        {
            // Evita fechas como el año 1800.
            return dateOfBirth.Year > (DateTime.Now.Year - 100);
        }
    }
}