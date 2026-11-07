using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Localization;

namespace AtelieMarylu.Web.Localization;

// Faz o mesmo que o DataAnnotationsValidator, mas traduz as mensagens de erro.
// O texto do ErrorMessage dos atributos é a chave da tradução.
public sealed class LocalizedDataAnnotationsValidator : ComponentBase, IDisposable
{
    private ValidationMessageStore? _messages;
    private EditContext? _context;

    [CascadingParameter] private EditContext? CurrentEditContext { get; set; }
    [Inject] private IStringLocalizer<SharedResource> Localizer { get; set; } = default!;

    protected override void OnParametersSet()
    {
        if (CurrentEditContext is null)
            throw new InvalidOperationException($"{nameof(LocalizedDataAnnotationsValidator)} precisa estar dentro de um EditForm.");
        if (ReferenceEquals(_context, CurrentEditContext)) return;

        DetachFromContext();
        _context = CurrentEditContext;
        _messages = new ValidationMessageStore(_context);
        _context.OnValidationRequested += OnValidationRequested;
        _context.OnFieldChanged += OnFieldChanged;
    }

    private void OnValidationRequested(object? sender, ValidationRequestedEventArgs e)
    {
        var context = _context!;
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(context.Model, new ValidationContext(context.Model), results, validateAllProperties: true);

        _messages!.Clear();
        foreach (var result in results)
        {
            foreach (var member in result.MemberNames.DefaultIfEmpty(string.Empty))
                _messages.Add(context.Field(member), Translate(result));
        }
        context.NotifyValidationStateChanged();
    }

    private void OnFieldChanged(object? sender, FieldChangedEventArgs e)
    {
        var field = e.FieldIdentifier;
        var property = field.Model.GetType().GetProperty(field.FieldName);
        if (property is null) return;

        var results = new List<ValidationResult>();
        var validation = new ValidationContext(field.Model) { MemberName = field.FieldName };
        Validator.TryValidateProperty(property.GetValue(field.Model), validation, results);

        _messages!.Clear(field);
        foreach (var result in results)
            _messages.Add(field, Translate(result));
        _context!.NotifyValidationStateChanged();
    }

    private string Translate(ValidationResult result) => Localizer[result.ErrorMessage ?? string.Empty];

    private void DetachFromContext()
    {
        if (_context is null) return;
        _context.OnValidationRequested -= OnValidationRequested;
        _context.OnFieldChanged -= OnFieldChanged;
        _messages?.Clear();
    }

    public void Dispose() => DetachFromContext();
}
