using PhotoGallery.Infrastructure.Models;
using PhotoGallery.Measure;

MeasureOptions? options = MeasureOptions.Parse(args, out string? problem);
if (options is null)
{
    Console.Error.WriteLine(problem);
    Console.Error.WriteLine(MeasureOptions.Usage);
    return 2;
}

// The processor unless asked otherwise: a measurement that takes longer is fine,
// and on a card the graphs must run one at a time in any case.
if (!options.UseGraphicsCard)
{
    Environment.SetEnvironmentVariable(InferenceDevice.Variable, "cpu");
}

return Measurement.Run(options);
