using System.Text;
using System.Text.Json;

var configName = ResolveConfigName(args);
var config = LoadConfig(configName);

var generator = new TerritoryGenerator();
var result = generator.Generate(config);

Console.WriteLine($"Territory config : {config.TerritoryName}");
Console.WriteLine($"Output dir       : {result.OutputDirectory}");
Console.WriteLine($"Geography seed   : {config.GeographySeed}");
Console.WriteLine($"History seed     : {config.HistorySeed}");
Console.WriteLine($"Regions          : {result.RegionCount}");
Console.WriteLine($"Territory span   : {result.TerritorySpan.X:0.00} x {result.TerritorySpan.Y:0.00} x {result.TerritorySpan.Z:0.00} ly");
Console.WriteLine($"SVG artifact     : {result.DiagramPath}");
Console.WriteLine($"Shaded SVG       : {result.ShadedDiagramPath}");
Console.WriteLine($"Heavy links SVG  : {result.HeavyLinkDiagramPath}");
Console.WriteLine($"Link report      : {result.RegionLinkReportPath}");
Console.WriteLine($"System report    : {result.SolarSystemReportPath}");
Console.WriteLine($"Path report      : {result.PathValidationReportPath}");
Console.WriteLine($"3D viewer        : {result.InteractiveViewerPath}");
Console.WriteLine($"Heavy links      : {result.HeavyLinkCount}");
Console.WriteLine($"Solar systems    : {result.SolarSystemCount}");
Console.WriteLine($"Status           : {result.Status}");
Console.WriteLine($"Message          : {result.Message}");

static string ResolveConfigName(string[] arguments)
{
    if (arguments.Length == 0)
    {
        return "TEST";
    }

    if (arguments.Length == 2 && arguments[0].Equals("--config", StringComparison.OrdinalIgnoreCase))
    {
        return arguments[1];
    }

    throw new ArgumentException("Usage: dotnet run -- [--config TEST|T0]");
}

static GeneratorConfig LoadConfig(string configName)
{
    var configPath = Path.Combine(AppContext.BaseDirectory, "config", $"{configName}.json");
    if (!File.Exists(configPath))
    {
        throw new FileNotFoundException($"Config file not found: {configPath}");
    }

    var json = File.ReadAllText(configPath);
    var options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    var config = JsonSerializer.Deserialize<GeneratorConfig>(json, options);
    if (config is null)
    {
        throw new InvalidOperationException($"Config file could not be deserialized: {configPath}");
    }

    return config;
}

sealed record GeneratorConfig(
    string TerritoryName,
    string OutputSubdirectory,
    string GeographySeed,
    string HistorySeed,
    int StarCount,
    double MinimumStarDistanceLy);

sealed class TerritoryGenerator
{
    public GenerationResult Generate(GeneratorConfig config)
    {
        var outputRoot = Path.Combine(Directory.GetCurrentDirectory(), "output");
        var outputDirectory = Path.Combine(outputRoot, config.OutputSubdirectory);
        Directory.CreateDirectory(outputDirectory);

        var structureGenerator = new TerritoryRegionStructureGenerator();
        var territory = structureGenerator.Generate(config);
        var networkSnapshot = TerritorySolarSystemValidationReportRenderer.BuildSnapshot(config, territory);

        var diagramPath = Path.Combine(outputDirectory, "DIAG_Territory_RegionStructure_VIEW.svg");
        var wireframeSvg = TerritoryRegionStructureSvgRenderer.Render(config, territory, shaded: false);
        File.WriteAllText(diagramPath, wireframeSvg, Encoding.UTF8);

        var shadedDiagramPath = Path.Combine(outputDirectory, "DIAG_Territory_RegionStructure_SHADED_VIEW.svg");
        var shadedSvg = TerritoryRegionStructureSvgRenderer.Render(config, territory, shaded: true);
        File.WriteAllText(shadedDiagramPath, shadedSvg, Encoding.UTF8);

        var sectorDiagnosticPaths = new List<string>(territory.RegionSectors.Count);
        foreach (var regionSectors in territory.RegionSectors.OrderBy(item => item.Region.Index))
        {
            var sectorPath = Path.Combine(outputDirectory, $"DIAG_{regionSectors.Region.Name}_SectorWireframe_VIEW.svg");
            var sectorSvg = TerritoryRegionStructureSvgRenderer.RenderRegionSectors(config, territory, regionSectors, networkSnapshot);
            File.WriteAllText(sectorPath, sectorSvg, Encoding.UTF8);
            sectorDiagnosticPaths.Add(sectorPath);

            var sectorViewerPath = Path.Combine(outputDirectory, $"DIAG_{regionSectors.Region.Name}_Sector3D_VIEW.html");
            var sectorViewerHtml = RegionSectorStructureHtmlRenderer.Render(config, regionSectors, networkSnapshot);
            File.WriteAllText(sectorViewerPath, sectorViewerHtml, Encoding.UTF8);

            foreach (var sector in regionSectors.Sectors.OrderBy(item => item.Index))
            {
                var navPath = Path.Combine(outputDirectory, $"NAV_{sector.Name}.svg");
                var navSvg = SectorNavigationMapSvgRenderer.Render(config, regionSectors, sector, networkSnapshot);
                File.WriteAllText(navPath, navSvg, Encoding.UTF8);

                var nav3dPath = Path.Combine(outputDirectory, $"NAV3D_{sector.Name}.html");
                var nav3dHtml = SectorNavigation3DHtmlRenderer.Render(config, regionSectors, sector, networkSnapshot);
                File.WriteAllText(nav3dPath, nav3dHtml, Encoding.UTF8);
            }
        }

        var heavyLinkDiagramPath = Path.Combine(outputDirectory, "DIAG_Territory_HeavyGateSystems_VIEW.svg");
        var heavyLinkSvg = TerritoryRegionStructureSvgRenderer.RenderHeavyLinks(config, territory);
        File.WriteAllText(heavyLinkDiagramPath, heavyLinkSvg, Encoding.UTF8);

        var regionLinkReportPath = Path.Combine(outputDirectory, "DIAG_REGION_LINKS.HTML");
        var regionLinkReportHtml = TerritoryRegionLinkReportRenderer.Render(config, territory);
        File.WriteAllText(regionLinkReportPath, regionLinkReportHtml, Encoding.UTF8);

        var solarSystemReportPath = Path.Combine(outputDirectory, "DIAG_SOLAR_SYSTEMS.HTML");
        var solarSystemReport = TerritorySolarSystemValidationReportRenderer.Build(config, territory);
        File.WriteAllText(solarSystemReportPath, solarSystemReport.Html, Encoding.UTF8);

        foreach (var stalePathReport in Directory.EnumerateFiles(outputDirectory, "DIAG_PATHS_FROM_*.HTML"))
        {
            File.Delete(stalePathReport);
        }

        var pathValidationReportPath = Path.Combine(outputDirectory, solarSystemReport.PathReportFileName);
        File.WriteAllText(pathValidationReportPath, solarSystemReport.PathReportHtml, Encoding.UTF8);

        var heavyGateViewerPath = Path.Combine(outputDirectory, "DIAG_Territory_HeavyGateSystems_3D_VIEW.html");
        var heavyGateViewerHtml = TerritoryHeavyGateNetworkHtmlRenderer.Render(config, territory);
        File.WriteAllText(heavyGateViewerPath, heavyGateViewerHtml, Encoding.UTF8);

        var interactiveViewerPath = Path.Combine(outputDirectory, "DIAG_Territory_StarMap_3D_VIEW.html");
        var interactiveViewerHtml = TerritoryStarMapHtmlRenderer.Render(config, territory, networkSnapshot);
        File.WriteAllText(interactiveViewerPath, interactiveViewerHtml, Encoding.UTF8);

        var territoryGateMapPath = Path.Combine(outputDirectory, "DIAG_Territory_StarGateMap_3D_VIEW.html");
        var territoryGateMapHtml = TerritoryStarGateMapHtmlRenderer.Render(config, territory, networkSnapshot);
        File.WriteAllText(territoryGateMapPath, territoryGateMapHtml, Encoding.UTF8);

        return new GenerationResult(
            OutputDirectory: outputDirectory,
            DiagramPath: diagramPath,
            ShadedDiagramPath: shadedDiagramPath,
            SectorDiagramPaths: sectorDiagnosticPaths,
            HeavyLinkDiagramPath: heavyLinkDiagramPath,
            RegionLinkReportPath: regionLinkReportPath,
            SolarSystemReportPath: solarSystemReportPath,
            PathValidationReportPath: pathValidationReportPath,
            InteractiveViewerPath: interactiveViewerPath,
            RegionCount: territory.Regions.Count,
            TerritorySpan: territory.Span,
            HeavyLinkCount: territory.HeavyGateLinks.Count,
            SolarSystemCount: solarSystemReport.SystemCount,
            Status: "generated",
            Message: $"Generated 16 seeded region cells, {territory.RegionSectors.Sum(item => item.Sectors.Count)} sector cells, {territory.HeavyGateLinks.Count} heavy-gate system pairs, and {solarSystemReport.SystemCount} solar systems for territory {config.TerritoryName}.");
    }
}

sealed class TerritoryRegionStructureGenerator
{
    private static readonly Span3 FixedSpan = new(130.0, 130.0, 65.0);
    private const int RegionCount = 16;
    private const int RelaxationIterations = 6;
    private const int RelaxationSamplesPerIteration = 6_000;
    private const int RegionOwnershipSampleCount = 18_000;
    private const int MinimumSectorsPerRegion = 4;
    private const int MaximumSectorsPerRegion = 9;
    private const int SectorRelaxationIterations = 5;
    private const int SurfaceLatitudeBands = 12;
    private const int SurfaceLongitudeBands = 24;

    public TerritoryRegionStructureData Generate(GeneratorConfig config)
    {
        var nucleusRandom = new Random(StableSeedHasher.HashToInt32($"{config.GeographySeed}:regions:nuclei"));
        var relaxationRandom = new Random(StableSeedHasher.HashToInt32($"{config.GeographySeed}:regions:relax"));
        var sectorRandom = new Random(StableSeedHasher.HashToInt32($"{config.GeographySeed}:regions:sectors"));
        var heavyGateRandom = new Random(StableSeedHasher.HashToInt32($"{config.GeographySeed}:regions:heavy-gates"));
        var mediumGateRandom = new Random(StableSeedHasher.HashToInt32($"{config.GeographySeed}:regions:medium-gates"));

        var nuclei = CreateInitialNuclei(nucleusRandom);
        for (var iteration = 0; iteration < RelaxationIterations; iteration++)
        {
            nuclei = RelaxNuclei(nuclei, relaxationRandom);
        }

        var regions = nuclei
            .Select((point, index) => new RegionCell(index, $"R{index:D2}", point, RegionPalette.GetColor(index)))
            .ToList();

        var regionOwnershipSamples = CreateRegionOwnershipSamples(regions, sectorRandom);
        var regionSectors = CreateRegionSectors(config, regions, regionOwnershipSamples, sectorRandom, mediumGateRandom);
        var surfaceMeshes = CreateSurfaceMeshes(regions);
        var heavyGateLinks = RegionHeavyGateGenerator.Generate(config, FixedSpan, regions, heavyGateRandom);

        return new TerritoryRegionStructureData(FixedSpan, regions, regionSectors, surfaceMeshes, heavyGateLinks);
    }

    private static List<Point3> CreateInitialNuclei(Random random)
    {
        var nuclei = new List<Point3>(RegionCount);
        var halfWidth = FixedSpan.X / 2.0;
        var halfHeight = FixedSpan.Y / 2.0;
        var halfDepth = FixedSpan.Z / 2.0;

        while (nuclei.Count < RegionCount)
        {
            var candidate = SamplePointInsideEllipsoid(random, halfWidth, halfHeight, halfDepth);
            if (nuclei.All(existing => DistanceSquared(existing, candidate) >= 196.0))
            {
                nuclei.Add(candidate);
            }
        }

        return nuclei;
    }

    private static List<Point3> RelaxNuclei(IReadOnlyList<Point3> nuclei, Random random)
    {
        var accumulators = new PointAccumulator[nuclei.Count];
        var halfWidth = FixedSpan.X / 2.0;
        var halfHeight = FixedSpan.Y / 2.0;
        var halfDepth = FixedSpan.Z / 2.0;

        for (var sampleIndex = 0; sampleIndex < RelaxationSamplesPerIteration; sampleIndex++)
        {
            var sample = SamplePointInsideEllipsoid(random, halfWidth, halfHeight, halfDepth);
            var nearestIndex = FindNearestRegionIndex(sample, nuclei);
            accumulators[nearestIndex].Add(sample);
        }

        var relaxed = new List<Point3>(nuclei.Count);
        for (var index = 0; index < nuclei.Count; index++)
        {
            if (accumulators[index].Count == 0)
            {
                relaxed.Add(nuclei[index]);
                continue;
            }

            relaxed.Add(accumulators[index].Average());
        }

        return relaxed;
    }

    private static int FindNearestRegionIndex(Point3 sample, IReadOnlyList<Point3> nuclei)
    {
        var nearestIndex = 0;
        var nearestDistanceSquared = double.MaxValue;

        for (var index = 0; index < nuclei.Count; index++)
        {
            var distanceSquared = DistanceSquared(sample, nuclei[index]);
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestIndex = index;
            }
        }

        return nearestIndex;
    }

    private static IReadOnlyList<RegionSurfaceMesh> CreateSurfaceMeshes(IReadOnlyList<RegionCell> regions)
    {
        var nuclei = regions.Select(region => region.Nucleus).ToList();
        var meshes = new List<RegionSurfaceMesh>(regions.Count);

        foreach (var region in regions)
        {
            meshes.Add(CreateSurfaceMesh(region, nuclei));
        }

        return meshes;
    }

    private static IReadOnlyList<OwnedPoint3> CreateRegionOwnershipSamples(IReadOnlyList<RegionCell> regions, Random random)
    {
        var halfWidth = FixedSpan.X / 2.0;
        var halfHeight = FixedSpan.Y / 2.0;
        var halfDepth = FixedSpan.Z / 2.0;
        var nuclei = regions.Select(region => region.Nucleus).ToList();
        var ownedSamples = new List<OwnedPoint3>(RegionOwnershipSampleCount);

        for (var sampleIndex = 0; sampleIndex < RegionOwnershipSampleCount; sampleIndex++)
        {
            var sample = SamplePointInsideEllipsoid(random, halfWidth, halfHeight, halfDepth);
            var ownerIndex = FindNearestRegionIndex(sample, nuclei);
            ownedSamples.Add(new OwnedPoint3(sample, ownerIndex));
        }

        return ownedSamples;
    }

    private static IReadOnlyList<RegionSectorSet> CreateRegionSectors(GeneratorConfig config, IReadOnlyList<RegionCell> regions, IReadOnlyList<OwnedPoint3> ownershipSamples, Random random, Random gateRandom)
    {
        var totalSamples = Math.Max(1, ownershipSamples.Count);
        var sectorSets = new List<RegionSectorSet>(regions.Count);

        foreach (var region in regions)
        {
            var regionSamples = ownershipSamples
                .Where(sample => sample.OwnerIndex == region.Index)
                .Select(sample => sample.Position)
                .ToList();

            if (regionSamples.Count == 0)
            {
                sectorSets.Add(new RegionSectorSet(region, new[] { new SectorCell(0, $"{region.Name}-S00", region.Nucleus, region.ColorHex) }, Array.Empty<OwnedPoint3>(), Array.Empty<SectorGateLink>()));
                continue;
            }

            var volumeRatio = (double)regionSamples.Count / totalSamples;
            var targetSectorCount = Math.Clamp((int)Math.Round(MinimumSectorsPerRegion + (volumeRatio * 32.0)), MinimumSectorsPerRegion, MaximumSectorsPerRegion);
            targetSectorCount = Math.Min(targetSectorCount, Math.Max(1, regionSamples.Count / 120));
            targetSectorCount = Math.Max(1, targetSectorCount);

            var sectorNuclei = CreateInitialSectorNuclei(regionSamples, targetSectorCount, random);
            for (var iteration = 0; iteration < SectorRelaxationIterations; iteration++)
            {
                sectorNuclei = RelaxSectorNuclei(sectorNuclei, regionSamples);
            }

            var ownedSectorSamples = new List<OwnedPoint3>(regionSamples.Count);
            foreach (var sample in regionSamples)
            {
                var ownerIndex = FindNearestSectorIndex(sample, sectorNuclei);
                ownedSectorSamples.Add(new OwnedPoint3(sample, ownerIndex));
            }

            var sectors = sectorNuclei
                .Select((nucleus, index) => new SectorCell(index, $"{region.Name}-S{index:D2}", nucleus, RegionPalette.GetSectorColor(region.Index, index)))
                .ToList();

            var sectorGateLinks = RegionSectorGateGenerator.Generate(config, region, sectors, ownedSectorSamples, gateRandom);
            sectorSets.Add(new RegionSectorSet(region, sectors, ownedSectorSamples, sectorGateLinks));
        }

        return sectorSets;
    }

    private static List<Point3> CreateInitialSectorNuclei(IReadOnlyList<Point3> regionSamples, int sectorCount, Random random)
    {
        var nuclei = new List<Point3>(sectorCount);
        var attempts = 0;

        while (nuclei.Count < sectorCount && attempts < regionSamples.Count * 4)
        {
            attempts++;
            var candidate = regionSamples[random.Next(regionSamples.Count)];
            if (nuclei.Count == 0 || nuclei.All(existing => DistanceSquared(existing, candidate) >= 36.0))
            {
                nuclei.Add(candidate);
            }
        }

        while (nuclei.Count < sectorCount)
        {
            nuclei.Add(regionSamples[random.Next(regionSamples.Count)]);
        }

        return nuclei;
    }

    private static List<Point3> RelaxSectorNuclei(IReadOnlyList<Point3> nuclei, IReadOnlyList<Point3> regionSamples)
    {
        var accumulators = new PointAccumulator[nuclei.Count];
        foreach (var sample in regionSamples)
        {
            var nearestIndex = FindNearestSectorIndex(sample, nuclei);
            accumulators[nearestIndex].Add(sample);
        }

        var relaxed = new List<Point3>(nuclei.Count);
        for (var index = 0; index < nuclei.Count; index++)
        {
            relaxed.Add(accumulators[index].Count == 0 ? nuclei[index] : accumulators[index].Average());
        }

        return relaxed;
    }

    private static int FindNearestSectorIndex(Point3 sample, IReadOnlyList<Point3> nuclei)
    {
        var nearestIndex = 0;
        var nearestDistanceSquared = double.MaxValue;

        for (var index = 0; index < nuclei.Count; index++)
        {
            var distanceSquared = DistanceSquared(sample, nuclei[index]);
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestIndex = index;
            }
        }

        return nearestIndex;
    }

    private static RegionSurfaceMesh CreateSurfaceMesh(RegionCell region, IReadOnlyList<Point3> nuclei)
    {
        var vertices = new List<Point3>((SurfaceLatitudeBands + 1) * SurfaceLongitudeBands);
        var quads = new List<MeshQuad>(SurfaceLatitudeBands * SurfaceLongitudeBands);

        for (var latitudeIndex = 0; latitudeIndex <= SurfaceLatitudeBands; latitudeIndex++)
        {
            var theta = Math.PI * latitudeIndex / SurfaceLatitudeBands;
            var sinTheta = Math.Sin(theta);
            var cosTheta = Math.Cos(theta);

            for (var longitudeIndex = 0; longitudeIndex < SurfaceLongitudeBands; longitudeIndex++)
            {
                var phi = (Math.PI * 2.0 * longitudeIndex) / SurfaceLongitudeBands;
                var direction = Normalize(new Point3(
                    sinTheta * Math.Cos(phi),
                    cosTheta,
                    sinTheta * Math.Sin(phi)));

                vertices.Add(FindSurfacePoint(region.Index, region.Nucleus, direction, nuclei));
            }
        }

        for (var latitudeIndex = 0; latitudeIndex < SurfaceLatitudeBands; latitudeIndex++)
        {
            var rowStart = latitudeIndex * SurfaceLongitudeBands;
            var nextRowStart = (latitudeIndex + 1) * SurfaceLongitudeBands;

            for (var longitudeIndex = 0; longitudeIndex < SurfaceLongitudeBands; longitudeIndex++)
            {
                var nextLongitude = (longitudeIndex + 1) % SurfaceLongitudeBands;
                quads.Add(new MeshQuad(
                    rowStart + longitudeIndex,
                    rowStart + nextLongitude,
                    nextRowStart + nextLongitude,
                    nextRowStart + longitudeIndex));
            }
        }

        return new RegionSurfaceMesh(region.Index, region.Name, region.ColorHex, vertices, quads);
    }

    private static Point3 FindSurfacePoint(int regionIndex, Point3 origin, Point3 direction, IReadOnlyList<Point3> nuclei)
    {
        var exitDistance = FindEllipsoidExitDistance(origin, direction);
        var low = 0.0;
        var high = exitDistance;

        for (var iteration = 0; iteration < 28; iteration++)
        {
            var midpoint = (low + high) * 0.5;
            var sample = new Point3(
                origin.X + (direction.X * midpoint),
                origin.Y + (direction.Y * midpoint),
                origin.Z + (direction.Z * midpoint));

            if (IsInsideTerritory(sample) && FindNearestRegionIndex(sample, nuclei) == regionIndex)
            {
                low = midpoint;
            }
            else
            {
                high = midpoint;
            }
        }

        return new Point3(
            origin.X + (direction.X * low),
            origin.Y + (direction.Y * low),
            origin.Z + (direction.Z * low));
    }

    private static double FindEllipsoidExitDistance(Point3 origin, Point3 direction)
    {
        var halfWidth = FixedSpan.X / 2.0;
        var halfHeight = FixedSpan.Y / 2.0;
        var halfDepth = FixedSpan.Z / 2.0;

        var a =
            ((direction.X * direction.X) / (halfWidth * halfWidth)) +
            ((direction.Y * direction.Y) / (halfHeight * halfHeight)) +
            ((direction.Z * direction.Z) / (halfDepth * halfDepth));

        var b = 2.0 * (
            ((origin.X * direction.X) / (halfWidth * halfWidth)) +
            ((origin.Y * direction.Y) / (halfHeight * halfHeight)) +
            ((origin.Z * direction.Z) / (halfDepth * halfDepth)));

        var c =
            ((origin.X * origin.X) / (halfWidth * halfWidth)) +
            ((origin.Y * origin.Y) / (halfHeight * halfHeight)) +
            ((origin.Z * origin.Z) / (halfDepth * halfDepth)) - 1.0;

        var discriminant = Math.Max(0.0, (b * b) - (4.0 * a * c));
        return (-b + Math.Sqrt(discriminant)) / (2.0 * a);
    }

    private static bool IsInsideTerritory(Point3 point)
    {
        var halfWidth = FixedSpan.X / 2.0;
        var halfHeight = FixedSpan.Y / 2.0;
        var halfDepth = FixedSpan.Z / 2.0;

        var normalized =
            (point.X * point.X) / (halfWidth * halfWidth) +
            (point.Y * point.Y) / (halfHeight * halfHeight) +
            (point.Z * point.Z) / (halfDepth * halfDepth);

        return normalized <= 1.0000001;
    }

    private static Point3 Normalize(Point3 point)
    {
        var length = Math.Sqrt((point.X * point.X) + (point.Y * point.Y) + (point.Z * point.Z));
        if (length <= 0.0)
        {
            return new Point3(0, 1, 0);
        }

        return new Point3(point.X / length, point.Y / length, point.Z / length);
    }

    private static Point3 SamplePointInsideEllipsoid(Random random, double halfWidth, double halfHeight, double halfDepth)
    {
        while (true)
        {
            var x = NextDouble(random, -halfWidth, halfWidth);
            var y = NextDouble(random, -halfHeight, halfHeight);
            var z = NextDouble(random, -halfDepth, halfDepth);

            var normalized =
                (x * x) / (halfWidth * halfWidth) +
                (y * y) / (halfHeight * halfHeight) +
                (z * z) / (halfDepth * halfDepth);

            if (normalized <= 1.0)
            {
                return new Point3(x, y, z);
            }
        }
    }

    private static double DistanceSquared(Point3 left, Point3 right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    private static double NextDouble(Random random, double min, double max) => min + (random.NextDouble() * (max - min));

    private struct PointAccumulator
    {
        private double _sumX;
        private double _sumY;
        private double _sumZ;

        public int Count { get; private set; }

        public void Add(Point3 point)
        {
            _sumX += point.X;
            _sumY += point.Y;
            _sumZ += point.Z;
            Count++;
        }

        public Point3 Average()
        {
            if (Count == 0)
            {
                return new Point3(0, 0, 0);
            }

            return new Point3(_sumX / Count, _sumY / Count, _sumZ / Count);
        }
    }
}

static class TerritoryRegionStructureSvgRenderer
{
    public static string Render(GeneratorConfig config, TerritoryRegionStructureData territory, bool shaded)
    {
        const int width = 1400;
        const int height = 980;
        var panels = CreateFullPagePanels(width, height);

        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
        builder.AppendLine("  <defs>");
        builder.AppendLine("    <linearGradient id=\"bg\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\">");
        builder.AppendLine("      <stop offset=\"0%\" stop-color=\"#07111f\" />");
        builder.AppendLine("      <stop offset=\"100%\" stop-color=\"#02060b\" />");
        builder.AppendLine("    </linearGradient>");
        builder.AppendLine("  </defs>");
        builder.AppendLine("  <rect width=\"100%\" height=\"100%\" fill=\"url(#bg)\" />");
        builder.AppendLine($"  <text x=\"40\" y=\"54\" fill=\"#e6eef8\" font-size=\"28\" font-family=\"Consolas, 'Courier New', monospace\">Territory Region Structure{(shaded ? " Shaded" : string.Empty)}</text>");
        builder.AppendLine($"  <text x=\"40\" y=\"82\" fill=\"#88a3bf\" font-size=\"15\" font-family=\"Consolas, 'Courier New', monospace\">{Escape(config.TerritoryName)} | Geography {Escape(config.GeographySeed)} | Region Cells 16 | Fixed Span 130 x 130 x 65 ly</text>");

        foreach (var panel in panels)
        {
            AppendPanel(builder, territory, panel, shaded);
        }

        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    public static string RenderHeavyLinks(GeneratorConfig config, TerritoryRegionStructureData territory)
    {
        const int width = 1400;
        const int height = 980;
        var panels = CreateFullPagePanels(width, height);

        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
        builder.AppendLine("  <defs>");
        builder.AppendLine("    <linearGradient id=\"bg\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\">");
        builder.AppendLine("      <stop offset=\"0%\" stop-color=\"#07111f\" />");
        builder.AppendLine("      <stop offset=\"100%\" stop-color=\"#02060b\" />");
        builder.AppendLine("    </linearGradient>");
        builder.AppendLine("  </defs>");
        builder.AppendLine("  <rect width=\"100%\" height=\"100%\" fill=\"url(#bg)\" />");
        builder.AppendLine("  <text x=\"40\" y=\"54\" fill=\"#e6eef8\" font-size=\"28\" font-family=\"Consolas, 'Courier New', monospace\">Territory Heavy Gate Systems</text>");
        builder.AppendLine($"  <text x=\"40\" y=\"82\" fill=\"#88a3bf\" font-size=\"15\" font-family=\"Consolas, 'Courier New', monospace\">{Escape(config.TerritoryName)} | Heavy pair span {config.MinimumStarDistanceLy * 9.0:0.0}-{config.MinimumStarDistanceLy * 12.0:0.0} ly | 2 adjacent minimum per region</text>");

        foreach (var panel in panels)
        {
            AppendHeavyLinkPanel(builder, territory, panel);
        }

        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    public static string RenderRegionSectors(GeneratorConfig config, TerritoryRegionStructureData territory, RegionSectorSet regionSectors, TerritoryNetworkSnapshot networkSnapshot)
    {
        const int width = 1400;
        const int height = 980;
        var panels = CreateFullPagePanels(width, height);

        var systemById = networkSnapshot.Systems.ToDictionary(system => system.Id);
        var regionSystems = networkSnapshot.Systems
            .Where(system => system.RegionIndex == regionSectors.Region.Index)
            .ToList();
        var regionLinks = networkSnapshot.Links
            .Where(link => systemById[link.SystemAId].RegionIndex == regionSectors.Region.Index && systemById[link.SystemBId].RegionIndex == regionSectors.Region.Index)
            .ToList();
        var lightCount = regionLinks.Count(link => link.GateType == "Light");
        var mediumCount = regionLinks.Count(link => link.GateType == "Medium");
        var heavyAnchorCount = regionSystems.Count(system => system.Gates.Any(gate => gate.GateType == "Heavy"));

        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
        builder.AppendLine("  <defs>");
        builder.AppendLine("    <linearGradient id=\"bg\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\">");
        builder.AppendLine("      <stop offset=\"0%\" stop-color=\"#07111f\" />");
        builder.AppendLine("      <stop offset=\"100%\" stop-color=\"#02060b\" />");
        builder.AppendLine("    </linearGradient>");
        builder.AppendLine("  </defs>");
        builder.AppendLine("  <rect width=\"100%\" height=\"100%\" fill=\"url(#bg)\" />");
        builder.AppendLine($"  <text x=\"40\" y=\"54\" fill=\"#e6eef8\" font-size=\"28\" font-family=\"Consolas, 'Courier New', monospace\">{Escape(regionSectors.Region.Name)} Sector Wireframe</text>");
        builder.AppendLine($"  <text x=\"40\" y=\"82\" fill=\"#88a3bf\" font-size=\"15\" font-family=\"Consolas, 'Courier New', monospace\">{Escape(config.TerritoryName)} | Parent Region {Escape(regionSectors.Region.Name)} | Systems {regionSystems.Count} | Light {lightCount} | Medium {mediumCount} | Heavy Anchors {heavyAnchorCount}</text>");

        foreach (var panel in panels)
        {
            AppendRegionSectorPanel(builder, regionSectors, regionSystems, regionLinks, panel);
        }

        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    private static void AppendPanel(StringBuilder builder, TerritoryRegionStructureData territory, SvgPanel panel, bool shaded)
    {
        var projectedNuclei = territory.Regions
            .Select(region => new ProjectedRegionCell(region, panel.Project(region.Nucleus)))
            .ToList();
        var envelope = BuildEnvelope(panel, territory.Span);
        var cells = BuildProjectedCells(projectedNuclei, envelope);
        var nucleusBounds = projectedNuclei.Select(region => region.ProjectedNucleus);
        var bounds = ProjectionBounds.From(envelope.Concat(nucleusBounds));
        var layout = PanelLayout.Create(panel, bounds);
        var origin = MapProjectedPoint(panel.Project(new Point3(0, 0, 0)), bounds, layout);
        var xAxisEnd = MapProjectedPoint(panel.Project(new Point3(1, 0, 0)), bounds, layout);
        var yAxisEnd = MapProjectedPoint(panel.Project(new Point3(0, 1, 0)), bounds, layout);
        var zAxisEnd = MapProjectedPoint(panel.Project(new Point3(0, 0, 1)), bounds, layout);

        builder.AppendLine($"  <g transform=\"translate({panel.X},{panel.Y})\">");
        builder.AppendLine($"    <rect width=\"{panel.Width}\" height=\"{panel.Height}\" rx=\"18\" fill=\"#081421\" stroke=\"#20364d\" stroke-width=\"1.5\" />");
        builder.AppendLine($"    <text x=\"18\" y=\"26\" fill=\"#d8e6f5\" font-size=\"18\" font-family=\"Consolas, 'Courier New', monospace\">{Escape(panel.Title)}</text>");
        AppendAxis(builder, origin, xAxisEnd, "#e35d6a", "X");
        AppendAxis(builder, origin, yAxisEnd, "#5da9e3", "Y");
        AppendAxis(builder, origin, zAxisEnd, "#f1c84c", "Z");

        var mappedEnvelope = envelope.Select(point => MapProjectedPoint(point, bounds, layout)).ToList();
        builder.AppendLine($"    <path d=\"{BuildClosedPath(mappedEnvelope)}\" fill=\"none\" stroke=\"#314b63\" stroke-width=\"1.2\" stroke-opacity=\"0.95\" />");

        var cellsInDrawOrder = cells
            .OrderBy(cell => panel.Depth(cell.Region.Nucleus))
            .ToList();

        if (shaded)
        {
            foreach (var cell in cellsInDrawOrder)
            {
                var mappedPolygon = cell.Polygon.Select(point => MapProjectedPoint(point, bounds, layout)).ToList();
                if (mappedPolygon.Count < 3)
                {
                    continue;
                }

                var depthOpacity = ComputeDepthOpacity(panel, territory.Span, cell.Region.Nucleus);
                builder.AppendLine($"    <path d=\"{BuildClosedPath(mappedPolygon)}\" fill=\"{cell.Region.ColorHex}\" fill-opacity=\"{depthOpacity:0.00}\" stroke=\"none\" />");
            }
        }

        foreach (var cell in cellsInDrawOrder)
        {
            var mappedPolygon = cell.Polygon.Select(point => MapProjectedPoint(point, bounds, layout)).ToList();
            if (mappedPolygon.Count >= 3)
            {
                var strokeOpacity = shaded ? 0.58 : 0.78;
                var strokeWidth = shaded ? 1.0 : 1.25;
                builder.AppendLine($"    <path d=\"{BuildClosedPath(mappedPolygon)}\" fill=\"none\" stroke=\"{cell.Region.ColorHex}\" stroke-width=\"{strokeWidth:0.00}\" stroke-opacity=\"{strokeOpacity:0.00}\" />");
            }
        }

        foreach (var region in projectedNuclei)
        {
            var mapped = MapProjectedPoint(region.ProjectedNucleus, bounds, layout);
            builder.AppendLine($"    <circle cx=\"{mapped.X:0.00}\" cy=\"{mapped.Y:0.00}\" r=\"4.40\" fill=\"{region.Region.ColorHex}\" stroke=\"#ffffff\" stroke-width=\"1.1\" />");
            builder.AppendLine($"    <text x=\"{mapped.X + 7:0.00}\" y=\"{mapped.Y - 7:0.00}\" fill=\"#f3f7fb\" font-size=\"11\" font-family=\"Consolas, 'Courier New', monospace\">{Escape(region.Region.Name)}</text>");
        }

        builder.AppendLine("  </g>");
    }

    private static void AppendHeavyLinkPanel(StringBuilder builder, TerritoryRegionStructureData territory, SvgPanel panel)
    {
        var projectedNuclei = territory.Regions
            .Select(region => new ProjectedRegionCell(region, panel.Project(region.Nucleus)))
            .ToList();
        var envelope = BuildEnvelope(panel, territory.Span);
        var cells = BuildProjectedCells(projectedNuclei, envelope);
        var bounds = ProjectionBounds.From(envelope.Concat(projectedNuclei.Select(region => region.ProjectedNucleus)));
        var layout = PanelLayout.Create(panel, bounds);

        builder.AppendLine($"  <g transform=\"translate({panel.X},{panel.Y})\">");
        builder.AppendLine($"    <rect width=\"{panel.Width}\" height=\"{panel.Height}\" rx=\"18\" fill=\"#081421\" stroke=\"#20364d\" stroke-width=\"1.5\" />");
        builder.AppendLine($"    <text x=\"18\" y=\"26\" fill=\"#d8e6f5\" font-size=\"18\" font-family=\"Consolas, 'Courier New', monospace\">{Escape(panel.Title)}</text>");

        foreach (var cell in cells.OrderBy(cell => panel.Depth(cell.Region.Nucleus)))
        {
            var mappedPolygon = cell.Polygon.Select(point => MapProjectedPoint(point, bounds, layout)).ToList();
            if (mappedPolygon.Count >= 3)
            {
                builder.AppendLine($"    <path d=\"{BuildClosedPath(mappedPolygon)}\" fill=\"none\" stroke=\"{cell.Region.ColorHex}\" stroke-width=\"1.00\" stroke-opacity=\"0.44\" />");
            }
        }

        foreach (var link in territory.HeavyGateLinks.OrderBy(link => panel.Depth(Midpoint(link.SystemA, link.SystemB))))
        {
            var start = MapProjectedPoint(panel.Project(link.SystemA), bounds, layout);
            var end = MapProjectedPoint(panel.Project(link.SystemB), bounds, layout);
            var stroke = link.IsAdjacent ? "#f4f1de" : "#f6bd60";
            var opacity = link.IsAdjacent ? "0.88" : "0.62";
            builder.AppendLine($"    <line x1=\"{start.X:0.00}\" y1=\"{start.Y:0.00}\" x2=\"{end.X:0.00}\" y2=\"{end.Y:0.00}\" stroke=\"{stroke}\" stroke-width=\"2.25\" stroke-opacity=\"{opacity}\" />");
            AppendStarSystem(builder, start, link.IsAdjacent ? "#f7f7f2" : "#ffe08a");
            AppendStarSystem(builder, end, link.IsAdjacent ? "#f7f7f2" : "#ffe08a");
        }

        builder.AppendLine("  </g>");
    }

    private static void AppendRegionSectorPanel(StringBuilder builder, RegionSectorSet regionSectors, IReadOnlyList<NetworkSystemNode> regionSystems, IReadOnlyList<NetworkLinkEdge> regionLinks, SvgPanel panel)
    {
        var projectedSectorNuclei = regionSectors.Sectors
            .Select(sector => new ProjectedSectorCell(sector, panel.Project(sector.Nucleus)))
            .ToList();
        var regionEnvelope = ComputeConvexHull(regionSectors.OwnedSamples.Select(sample => panel.Project(sample.Position)).ToList());
        var cells = BuildProjectedSectorCells(projectedSectorNuclei, regionEnvelope);
        var projectedNetworkPoints = regionSystems.Select(system => panel.Project(system.Position)).ToList();
        var bounds = ProjectionBounds.From(regionEnvelope.Concat(projectedSectorNuclei.Select(item => item.ProjectedNucleus)).Concat(projectedNetworkPoints));
        var layout = PanelLayout.Create(panel, bounds);
        var origin = MapProjectedPoint(panel.Project(new Point3(0, 0, 0)), bounds, layout);
        var xAxisEnd = MapProjectedPoint(panel.Project(new Point3(1, 0, 0)), bounds, layout);
        var yAxisEnd = MapProjectedPoint(panel.Project(new Point3(0, 1, 0)), bounds, layout);
        var zAxisEnd = MapProjectedPoint(panel.Project(new Point3(0, 0, 1)), bounds, layout);

        builder.AppendLine($"  <g transform=\"translate({panel.X},{panel.Y})\">");
        builder.AppendLine($"    <rect width=\"{panel.Width}\" height=\"{panel.Height}\" rx=\"18\" fill=\"#081421\" stroke=\"#20364d\" stroke-width=\"1.5\" />");
        builder.AppendLine($"    <text x=\"18\" y=\"26\" fill=\"#d8e6f5\" font-size=\"18\" font-family=\"Consolas, 'Courier New', monospace\">{Escape(panel.Title)}</text>");
        AppendAxis(builder, origin, xAxisEnd, "#e35d6a", "X");
        AppendAxis(builder, origin, yAxisEnd, "#5da9e3", "Y");
        AppendAxis(builder, origin, zAxisEnd, "#f1c84c", "Z");

        var mappedEnvelope = regionEnvelope.Select(point => MapProjectedPoint(point, bounds, layout)).ToList();
        if (mappedEnvelope.Count >= 3)
        {
            builder.AppendLine($"    <path d=\"{BuildClosedPath(mappedEnvelope)}\" fill=\"none\" stroke=\"#314b63\" stroke-width=\"1.2\" stroke-opacity=\"0.95\" />");
        }

        foreach (var cell in cells.OrderBy(cell => panel.Depth(cell.Sector.Nucleus)))
        {
            var mappedPolygon = cell.Polygon.Select(point => MapProjectedPoint(point, bounds, layout)).ToList();
            if (mappedPolygon.Count >= 3)
            {
                builder.AppendLine($"    <path d=\"{BuildClosedPath(mappedPolygon)}\" fill=\"{cell.Sector.ColorHex}\" fill-opacity=\"0.16\" stroke=\"{cell.Sector.ColorHex}\" stroke-width=\"1.05\" stroke-opacity=\"0.88\" />");
            }
        }

        var systemById = regionSystems.ToDictionary(system => system.Id);
        foreach (var link in regionLinks.OrderBy(link => panel.Depth(Midpoint(systemById[link.SystemAId].Position, systemById[link.SystemBId].Position))))
        {
            var start = MapProjectedPoint(panel.Project(systemById[link.SystemAId].Position), bounds, layout);
            var end = MapProjectedPoint(panel.Project(systemById[link.SystemBId].Position), bounds, layout);
            builder.AppendLine($"    <line x1=\"{start.X:0.00}\" y1=\"{start.Y:0.00}\" x2=\"{end.X:0.00}\" y2=\"{end.Y:0.00}\" stroke=\"{GetGateStroke(link.GateType)}\" stroke-width=\"{GetGateStrokeWidth(link.GateType):0.00}\" stroke-opacity=\"{GetGateStrokeOpacity(link.GateType):0.00}\" />");
        }

        foreach (var system in regionSystems.OrderBy(system => panel.Depth(system.Position)))
        {
            var mapped = MapProjectedPoint(panel.Project(system.Position), bounds, layout);
            var sectorColor = regionSectors.Sectors.First(sector => sector.Index == system.SectorIndex).ColorHex;
            AppendNetworkSystem(builder, mapped, sectorColor, system);
        }

        foreach (var sector in projectedSectorNuclei)
        {
            var mapped = MapProjectedPoint(sector.ProjectedNucleus, bounds, layout);
            builder.AppendLine($"    <circle cx=\"{mapped.X:0.00}\" cy=\"{mapped.Y:0.00}\" r=\"3.40\" fill=\"{sector.Sector.ColorHex}\" stroke=\"#ffffff\" stroke-width=\"0.9\" />");
            builder.AppendLine($"    <text x=\"{mapped.X + 6:0.00}\" y=\"{mapped.Y - 6:0.00}\" fill=\"#f3f7fb\" font-size=\"10\" font-family=\"Consolas, 'Courier New', monospace\">{Escape(sector.Sector.Name)}</text>");
        }

        builder.AppendLine("  </g>");
    }

    private static void AppendStarSystem(StringBuilder builder, Point2 point, string glow)
    {
        builder.AppendLine($"    <circle cx=\"{point.X:0.00}\" cy=\"{point.Y:0.00}\" r=\"5.60\" fill=\"{glow}\" fill-opacity=\"0.16\" />");
        builder.AppendLine($"    <circle cx=\"{point.X:0.00}\" cy=\"{point.Y:0.00}\" r=\"2.20\" fill=\"#ffffff\" stroke=\"{glow}\" stroke-width=\"0.90\" />");
    }

    private static void AppendNetworkSystem(StringBuilder builder, Point2 point, string sectorColor, NetworkSystemNode system)
    {
        var gateTypes = system.Gates.Select(gate => gate.GateType).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var hasHeavy = gateTypes.Contains("Heavy", StringComparer.OrdinalIgnoreCase);
        var hasMedium = gateTypes.Contains("Medium", StringComparer.OrdinalIgnoreCase);
        var radius = hasHeavy ? 3.3 : hasMedium ? 2.8 : system.SourceType.Contains("Connector", StringComparison.OrdinalIgnoreCase) ? 1.5 : 2.1;
        var stroke = hasHeavy ? "#ffd166" : hasMedium ? "#8fd3ff" : "#f3f7fb";
        var glow = hasHeavy ? "#ffd166" : hasMedium ? "#8fd3ff" : sectorColor;
        builder.AppendLine($"    <circle cx=\"{point.X:0.00}\" cy=\"{point.Y:0.00}\" r=\"{radius * 2.0:0.00}\" fill=\"{glow}\" fill-opacity=\"0.12\" />");
        builder.AppendLine($"    <circle cx=\"{point.X:0.00}\" cy=\"{point.Y:0.00}\" r=\"{radius:0.00}\" fill=\"{sectorColor}\" stroke=\"{stroke}\" stroke-width=\"0.85\" />");
    }

    private static string GetGateStroke(string gateType)
    {
        return gateType.Equals("Heavy", StringComparison.OrdinalIgnoreCase)
            ? "#f6bd60"
            : gateType.Equals("Medium", StringComparison.OrdinalIgnoreCase)
                ? "#9ed8ff"
                : "#b7f0c7";
    }

    private static double GetGateStrokeWidth(string gateType)
    {
        return gateType.Equals("Heavy", StringComparison.OrdinalIgnoreCase)
            ? 2.40
            : gateType.Equals("Medium", StringComparison.OrdinalIgnoreCase)
                ? 2.00
                : 1.20;
    }

    private static double GetGateStrokeOpacity(string gateType)
    {
        return gateType.Equals("Heavy", StringComparison.OrdinalIgnoreCase)
            ? 0.82
            : gateType.Equals("Medium", StringComparison.OrdinalIgnoreCase)
                ? 0.84
                : 0.58;
    }

    private static Point3 Midpoint(Point3 left, Point3 right)
    {
        return new Point3((left.X + right.X) * 0.5, (left.Y + right.Y) * 0.5, (left.Z + right.Z) * 0.5);
    }

    private static Point2 ProjectAngled(Point3 point)
    {
        var x = (point.X - point.Y) * 0.8660254038;
        var y = ((point.X + point.Y) * 0.5) - (point.Z * 1.2);
        return new Point2(x, -y);
    }

    private static double DepthAngled(Point3 point)
    {
        return (point.X * 0.35) + (point.Y * 0.35) + (point.Z * 0.30);
    }

    private static Point2 MapProjectedPoint(Point2 point, ProjectionBounds bounds, PanelLayout layout)
    {
        return new Point2(
            layout.OffsetX + ((point.X - bounds.MinX) * layout.Scale),
            layout.OffsetY + ((bounds.MaxY - point.Y) * layout.Scale));
    }

    private static List<Point2> BuildEnvelope(SvgPanel panel, Span3 span)
    {
        if (panel.Title == "Top Down")
        {
            return BuildEllipsePolygon(span.X / 2.0, span.Y / 2.0, z: 0.0, panel, useVerticalAsZ: false);
        }

        if (panel.Title == "Side")
        {
            return BuildSideEllipsePolygon(span.X / 2.0, span.Z / 2.0, panel);
        }

        return BuildProjectedHull(panel, span);
    }

    private static List<Point2> BuildEllipsePolygon(double radiusX, double radiusY, double z, SvgPanel panel, bool useVerticalAsZ)
    {
        var polygon = new List<Point2>(96);
        for (var index = 0; index < 96; index++)
        {
            var angle = (Math.PI * 2.0 * index) / 96.0;
            var x = Math.Cos(angle) * radiusX;
            var y = Math.Sin(angle) * radiusY;
            var point = useVerticalAsZ
                ? new Point3(x, 0.0, y)
                : new Point3(x, y, z);
            polygon.Add(panel.Project(point));
        }

        return polygon;
    }

    private static List<Point2> BuildSideEllipsePolygon(double radiusX, double radiusZ, SvgPanel panel)
    {
        var polygon = new List<Point2>(96);
        for (var index = 0; index < 96; index++)
        {
            var angle = (Math.PI * 2.0 * index) / 96.0;
            var x = Math.Cos(angle) * radiusX;
            var z = Math.Sin(angle) * radiusZ;
            polygon.Add(panel.Project(new Point3(x, 0.0, z)));
        }

        return polygon;
    }

    private static List<Point2> BuildProjectedHull(SvgPanel panel, Span3 span)
    {
        var surfacePoints = new List<Point2>(512);
        var radiusX = span.X / 2.0;
        var radiusY = span.Y / 2.0;
        var radiusZ = span.Z / 2.0;

        for (var latitudeIndex = 0; latitudeIndex <= 16; latitudeIndex++)
        {
            var latitude = (-Math.PI / 2.0) + (Math.PI * latitudeIndex / 16.0);
            var cosLatitude = Math.Cos(latitude);
            var sinLatitude = Math.Sin(latitude);
            for (var longitudeIndex = 0; longitudeIndex < 32; longitudeIndex++)
            {
                var longitude = (Math.PI * 2.0 * longitudeIndex) / 32.0;
                var x = radiusX * cosLatitude * Math.Cos(longitude);
                var y = radiusY * cosLatitude * Math.Sin(longitude);
                var z = radiusZ * sinLatitude;
                surfacePoints.Add(panel.Project(new Point3(x, y, z)));
            }
        }

        return ComputeConvexHull(surfacePoints);
    }

    private static List<ProjectedCell> BuildProjectedCells(IReadOnlyList<ProjectedRegionCell> nuclei, List<Point2> envelope)
    {
        var cells = new List<ProjectedCell>(nuclei.Count);
        foreach (var cell in nuclei)
        {
            var polygon = new List<Point2>(envelope);
            foreach (var other in nuclei)
            {
                if (other.Region.Index == cell.Region.Index)
                {
                    continue;
                }

                polygon = ClipPolygonToCloserSide(polygon, cell.ProjectedNucleus, other.ProjectedNucleus);
                if (polygon.Count == 0)
                {
                    break;
                }
            }

            cells.Add(new ProjectedCell(cell.Region, polygon));
        }

        return cells;
    }

    private static List<ProjectedSectorPolygon> BuildProjectedSectorCells(IReadOnlyList<ProjectedSectorCell> nuclei, List<Point2> envelope)
    {
        var cells = new List<ProjectedSectorPolygon>(nuclei.Count);
        foreach (var cell in nuclei)
        {
            var polygon = new List<Point2>(envelope);
            foreach (var other in nuclei)
            {
                if (other.Sector.Index == cell.Sector.Index)
                {
                    continue;
                }

                polygon = ClipPolygonToCloserSide(polygon, cell.ProjectedNucleus, other.ProjectedNucleus);
                if (polygon.Count == 0)
                {
                    break;
                }
            }

            cells.Add(new ProjectedSectorPolygon(cell.Sector, polygon));
        }

        return cells;
    }

    private static List<Point2> ClipPolygonToCloserSide(IReadOnlyList<Point2> polygon, Point2 seed, Point2 other)
    {
        if (polygon.Count == 0)
        {
            return new List<Point2>();
        }

        var result = new List<Point2>();
        var mid = new Point2((seed.X + other.X) / 2.0, (seed.Y + other.Y) / 2.0);
        var normal = new Point2(other.X - seed.X, other.Y - seed.Y);

        for (var index = 0; index < polygon.Count; index++)
        {
            var current = polygon[index];
            var next = polygon[(index + 1) % polygon.Count];
            var currentInside = IsInsideHalfPlane(current, mid, normal);
            var nextInside = IsInsideHalfPlane(next, mid, normal);

            if (currentInside && nextInside)
            {
                result.Add(next);
                continue;
            }

            if (currentInside && !nextInside)
            {
                result.Add(IntersectSegmentWithBisector(current, next, mid, normal));
                continue;
            }

            if (!currentInside && nextInside)
            {
                result.Add(IntersectSegmentWithBisector(current, next, mid, normal));
                result.Add(next);
            }
        }

        return result;
    }

    private static bool IsInsideHalfPlane(Point2 point, Point2 mid, Point2 normal)
    {
        var value = ((point.X - mid.X) * normal.X) + ((point.Y - mid.Y) * normal.Y);
        return value <= 0.000001;
    }

    private static Point2 IntersectSegmentWithBisector(Point2 start, Point2 end, Point2 mid, Point2 normal)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var denominator = (dx * normal.X) + (dy * normal.Y);
        if (Math.Abs(denominator) < 0.000001)
        {
            return start;
        }

        var numerator = ((mid.X - start.X) * normal.X) + ((mid.Y - start.Y) * normal.Y);
        var t = numerator / denominator;
        return new Point2(start.X + (dx * t), start.Y + (dy * t));
    }

    private static List<Point2> ComputeConvexHull(List<Point2> points)
    {
        var sorted = points
            .OrderBy(point => point.X)
            .ThenBy(point => point.Y)
            .ToList();

        if (sorted.Count <= 1)
        {
            return sorted;
        }

        var lower = new List<Point2>();
        foreach (var point in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], point) <= 0)
            {
                lower.RemoveAt(lower.Count - 1);
            }

            lower.Add(point);
        }

        var upper = new List<Point2>();
        for (var index = sorted.Count - 1; index >= 0; index--)
        {
            var point = sorted[index];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], point) <= 0)
            {
                upper.RemoveAt(upper.Count - 1);
            }

            upper.Add(point);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static double Cross(Point2 origin, Point2 left, Point2 right)
    {
        return ((left.X - origin.X) * (right.Y - origin.Y)) - ((left.Y - origin.Y) * (right.X - origin.X));
    }

    private static string BuildClosedPath(IReadOnlyList<Point2> polygon)
    {
        if (polygon.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append($"M {polygon[0].X:0.00} {polygon[0].Y:0.00}");
        for (var index = 1; index < polygon.Count; index++)
        {
            builder.Append($" L {polygon[index].X:0.00} {polygon[index].Y:0.00}");
        }

        builder.Append(" Z");
        return builder.ToString();
    }

    private static double ComputeDepthOpacity(SvgPanel panel, Span3 span, Point3 nucleus)
    {
        var halfSpan = panel.Title switch
        {
            "Top Down" => span.Z / 2.0,
            "Side" => span.Y / 2.0,
            _ => Math.Max(span.X, Math.Max(span.Y, span.Z)) / 2.0
        };

        var depthValue = panel.Depth(nucleus);
        var normalized = halfSpan <= 0.0 ? 0.5 : (depthValue + halfSpan) / (halfSpan * 2.0);
        return 0.12 + (Math.Clamp(normalized, 0.0, 1.0) * 0.18);
    }

    private static void AppendAxis(StringBuilder builder, Point2 origin, Point2 axisEnd, string color, string label)
    {
        var dx = axisEnd.X - origin.X;
        var dy = axisEnd.Y - origin.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 0.001)
        {
            builder.AppendLine($"    <circle cx=\"{origin.X:0.00}\" cy=\"{origin.Y:0.00}\" r=\"2.25\" fill=\"{color}\" fill-opacity=\"0.75\" />");
            builder.AppendLine($"    <text x=\"{origin.X + 6:0.00}\" y=\"{origin.Y - 6:0.00}\" fill=\"{color}\" font-size=\"12\" font-family=\"Consolas, 'Courier New', monospace\">{label}</text>");
            return;
        }

        var scale = 52.0 / length;
        var endX = origin.X + (dx * scale);
        var endY = origin.Y + (dy * scale);
        builder.AppendLine($"    <line x1=\"{origin.X:0.00}\" y1=\"{origin.Y:0.00}\" x2=\"{endX:0.00}\" y2=\"{endY:0.00}\" stroke=\"{color}\" stroke-width=\"1.5\" stroke-opacity=\"0.85\" />");
        builder.AppendLine($"    <text x=\"{endX + 6:0.00}\" y=\"{endY - 6:0.00}\" fill=\"{color}\" font-size=\"12\" font-family=\"Consolas, 'Courier New', monospace\">{label}</text>");
    }

    private static string Escape(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    private static SvgPanel[] CreateFullPagePanels(int width, int height)
    {
        const double horizontalMargin = 32.0;
        const double topOffset = 120.0;
        const double bottomMargin = 28.0;
        const double panelGap = 24.0;

        var panelWidth = (width - (horizontalMargin * 2.0) - (panelGap * 2.0)) / 3.0;
        var panelHeight = height - topOffset - bottomMargin;
        return new[]
        {
            new SvgPanel("Top Down", horizontalMargin, topOffset, panelWidth, panelHeight, point => new Point2(point.X, -point.Y), point => point.Z),
            new SvgPanel("Side", horizontalMargin + panelWidth + panelGap, topOffset, panelWidth, panelHeight, point => new Point2(point.X, -point.Z), point => point.Y),
            new SvgPanel("Angled", horizontalMargin + (panelWidth * 2.0) + (panelGap * 2.0), topOffset, panelWidth, panelHeight, ProjectAngled, DepthAngled)
        };
    }

    private sealed record SvgPanel(
        string Title,
        double X,
        double Y,
        double Width,
        double Height,
        Func<Point3, Point2> Project,
        Func<Point3, double> Depth);

    private sealed record ProjectedRegionCell(RegionCell Region, Point2 ProjectedNucleus);
    private sealed record ProjectedCell(RegionCell Region, IReadOnlyList<Point2> Polygon);
    private sealed record ProjectedSectorCell(SectorCell Sector, Point2 ProjectedNucleus);
    private sealed record ProjectedSectorPolygon(SectorCell Sector, IReadOnlyList<Point2> Polygon);

    private sealed record PanelLayout(double Scale, double OffsetX, double OffsetY)
    {
        public static PanelLayout Create(SvgPanel panel, ProjectionBounds bounds)
        {
            const double leftPadding = 18;
            const double rightPadding = 18;
            const double topPadding = 42;
            const double bottomPadding = 18;

            var plotWidth = panel.Width - leftPadding - rightPadding;
            var plotHeight = panel.Height - topPadding - bottomPadding;
            var sourceWidth = Math.Max(bounds.MaxX - bounds.MinX, 1.0);
            var sourceHeight = Math.Max(bounds.MaxY - bounds.MinY, 1.0);
            var scale = Math.Min(plotWidth / sourceWidth, plotHeight / sourceHeight);
            var drawnWidth = sourceWidth * scale;
            var drawnHeight = sourceHeight * scale;
            var offsetX = leftPadding + ((plotWidth - drawnWidth) / 2.0);
            var offsetY = topPadding + ((plotHeight - drawnHeight) / 2.0);

            return new PanelLayout(scale, offsetX, offsetY);
        }
    }
}

static class TerritoryRegionStructureHtmlRenderer
{
    public static string Render(GeneratorConfig config, TerritoryRegionStructureData territory)
    {
        var meshesJson = BuildSurfaceMeshesJson(territory);
        var spanJson = $"{{\"x\":{territory.Span.X:0.###},\"y\":{territory.Span.Y:0.###},\"z\":{territory.Span.Z:0.###}}}";

        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"UTF-8\" />");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />");
        builder.AppendLine($"  <title>{Escape(config.TerritoryName)} Region Structure 3D</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    :root { color-scheme: dark; }");
        builder.AppendLine("    body { margin: 0; background: radial-gradient(circle at top, #0b1626, #04070d 65%); color: #e7eef7; font-family: Consolas, 'Courier New', monospace; }");
        builder.AppendLine("    .shell { padding: 24px 28px 18px; }");
        builder.AppendLine("    h1 { margin: 0 0 8px; font-size: 28px; font-weight: 500; }");
        builder.AppendLine("    .meta { color: #8fa6be; font-size: 14px; margin-bottom: 14px; }");
        builder.AppendLine("    .hint { color: #9db4ca; font-size: 13px; margin-bottom: 14px; }");
        builder.AppendLine("    canvas { width: min(1200px, calc(100vw - 56px)); height: min(840px, calc(100vh - 160px)); border: 1px solid #22384d; border-radius: 18px; background: linear-gradient(180deg, rgba(10,20,34,0.92), rgba(3,7,12,0.96)); display: block; box-shadow: 0 18px 48px rgba(0,0,0,0.32); cursor: grab; }");
        builder.AppendLine("    canvas.dragging { cursor: grabbing; }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("  <div class=\"shell\" id=\"viewer-shell\">");
        builder.AppendLine($"    <h1>{Escape(config.TerritoryName)} Region Structure 3D</h1>");
        builder.AppendLine($"    <div class=\"meta\">Geography {Escape(config.GeographySeed)} | Region Cells 16 | Fixed Span 130 x 130 x 65 ly</div>");
        builder.AppendLine("    <div class=\"hint\">Left-drag to rotate. Right-drag or Shift-drag to pan. W/S dolly in and out. Scroll to zoom. Double-click to reset view. This diagnostic renders translucent region cell surfaces only.</div>");
        builder.AppendLine("    <canvas id=\"view\" width=\"1200\" height=\"840\"></canvas>");
        builder.AppendLine("  </div>");
        builder.AppendLine("  <script>");
        builder.AppendLine($"    const span = {spanJson};");
        builder.AppendLine($"    const meshes = {meshesJson};");
        builder.AppendLine("    const shell = document.getElementById('viewer-shell');");
        builder.AppendLine("    const canvas = document.getElementById('view');");
        builder.AppendLine("    const ctx = canvas.getContext('2d');");
        builder.AppendLine("    let yaw = -0.72;");
        builder.AppendLine("    let pitch = 0.48;");
        builder.AppendLine("    let zoom = 4.2;");
        builder.AppendLine("    let panX = 0;");
        builder.AppendLine("    let panY = 0;");
        builder.AppendLine("    let dolly = 0;");
        builder.AppendLine("    let dragging = false;");
        builder.AppendLine("    let dragMode = 'rotate';");
        builder.AppendLine("    let zoomTargetActive = false;");
        builder.AppendLine("    let lastX = 0;");
        builder.AppendLine("    let lastY = 0;");
        builder.AppendLine("    const sceneExtent = Math.max(span.x, span.y, span.z);");
        builder.AppendLine("    const distanceBase = Math.max(96, sceneExtent * 0.85);");
        builder.AppendLine("    function resetView() { yaw = -0.72; pitch = 0.48; zoom = 4.2; panX = 0; panY = 0; dolly = 0; render(); }");
        builder.AppendLine("    function applyZoom(deltaY) { const factor = Math.exp(deltaY * 0.0030); zoom = Math.max(0.01, Math.min(48.0, zoom * factor)); render(); }");
        builder.AppendLine("    function rotate(point) {");
        builder.AppendLine("      const cy = Math.cos(yaw), sy = Math.sin(yaw);");
        builder.AppendLine("      const cp = Math.cos(pitch), sp = Math.sin(pitch);");
        builder.AppendLine("      const x1 = (point.x * cy) - (point.z * sy);");
        builder.AppendLine("      const z1 = (point.x * sy) + (point.z * cy);");
        builder.AppendLine("      const y2 = (point.y * cp) - (z1 * sp);");
        builder.AppendLine("      const z2 = (point.y * sp) + (z1 * cp);");
        builder.AppendLine("      return { x: x1, y: y2, z: z2, color: point.color, label: point.label, regionIndex: point.regionIndex, isNucleus: point.isNucleus === true };");
        builder.AppendLine("    }");
        builder.AppendLine("    function project(point) {");
        builder.AppendLine("      const distance = distanceBase + (85 * zoom);");
        builder.AppendLine("      const shiftedZ = Math.min(point.z + dolly, distance - 2.0);");
        builder.AppendLine("      const perspective = distance / (distance - shiftedZ);");
        builder.AppendLine("      return {");
        builder.AppendLine("        x: canvas.width / 2 + panX + (point.x * perspective * 3.1),");
        builder.AppendLine("        y: canvas.height / 2 + panY + (point.y * perspective * 3.1),");
        builder.AppendLine("        z: shiftedZ,");
        builder.AppendLine("        scale: perspective,");
        builder.AppendLine("        color: point.color,");
        builder.AppendLine("        label: point.label,");
        builder.AppendLine("        isNucleus: point.isNucleus,");
        builder.AppendLine("        regionIndex: point.regionIndex");
        builder.AppendLine("      }; }");
        builder.AppendLine("    function render() {");
        builder.AppendLine("      ctx.clearRect(0, 0, canvas.width, canvas.height);");
        builder.AppendLine("      ctx.fillStyle = '#07101b';");
        builder.AppendLine("      ctx.fillRect(0, 0, canvas.width, canvas.height);");
        builder.AppendLine("      const surfaces = [];");
        builder.AppendLine("      for (const mesh of meshes) {");
        builder.AppendLine("        const projectedVertices = mesh.vertices.map(rotate).map(project);");
        builder.AppendLine("        for (const quad of mesh.quads) {");
        builder.AppendLine("          const a = projectedVertices[quad[0]];");
        builder.AppendLine("          const b = projectedVertices[quad[1]];");
        builder.AppendLine("          const c = projectedVertices[quad[2]];");
        builder.AppendLine("          const d = projectedVertices[quad[3]];");
        builder.AppendLine("          const depth = (a.z + b.z + c.z + d.z) * 0.25;");
        builder.AppendLine("          surfaces.push({ color: mesh.color, depth, points: [a, b, c, d] });");
        builder.AppendLine("        }");
        builder.AppendLine("      }");
        builder.AppendLine("      surfaces.sort((left, right) => left.depth - right.depth);");
        builder.AppendLine("      for (const surface of surfaces) {");
        builder.AppendLine("        ctx.fillStyle = hexToRgba(surface.color, 0.18);");
        builder.AppendLine("        ctx.strokeStyle = hexToRgba(surface.color, 0.72);");
        builder.AppendLine("        ctx.lineWidth = 1.05;");
        builder.AppendLine("        ctx.beginPath();");
        builder.AppendLine("        ctx.moveTo(surface.points[0].x, surface.points[0].y);");
        builder.AppendLine("        for (let index = 1; index < surface.points.length; index += 1) {");
        builder.AppendLine("          ctx.lineTo(surface.points[index].x, surface.points[index].y);");
        builder.AppendLine("        }");
        builder.AppendLine("        ctx.closePath();");
        builder.AppendLine("        ctx.fill();");
        builder.AppendLine("        ctx.stroke();");
        builder.AppendLine("      }");
        builder.AppendLine("    }");
        builder.AppendLine("    function hexToRgba(hex, alpha) {");
        builder.AppendLine("      const normalized = hex.replace('#', '');");
        builder.AppendLine("      const r = parseInt(normalized.slice(0, 2), 16);");
        builder.AppendLine("      const g = parseInt(normalized.slice(2, 4), 16);");
        builder.AppendLine("      const b = parseInt(normalized.slice(4, 6), 16);");
        builder.AppendLine("      return `rgba(${r}, ${g}, ${b}, ${alpha})`;");
        builder.AppendLine("    }");
        builder.AppendLine("    canvas.addEventListener('pointerdown', event => { dragging = true; dragMode = (event.button === 2 || event.shiftKey) ? 'pan' : 'rotate'; lastX = event.clientX; lastY = event.clientY; canvas.classList.add('dragging'); });");
        builder.AppendLine("    canvas.addEventListener('contextmenu', event => event.preventDefault());");
        builder.AppendLine("    shell.addEventListener('pointerenter', () => { zoomTargetActive = true; });");
        builder.AppendLine("    shell.addEventListener('pointerleave', () => { zoomTargetActive = false; });");
        builder.AppendLine("    window.addEventListener('pointerup', () => { dragging = false; canvas.classList.remove('dragging'); });");
        builder.AppendLine("    window.addEventListener('pointermove', event => { if (!dragging) return; const dx = event.clientX - lastX; const dy = event.clientY - lastY; lastX = event.clientX; lastY = event.clientY; if (dragMode === 'pan') { panX += dx; panY += dy; } else { yaw += dx * 0.008; pitch = Math.max(-1.35, Math.min(1.35, pitch + dy * 0.008)); } render(); });");
        builder.AppendLine("    shell.addEventListener('wheel', event => { event.preventDefault(); applyZoom(event.deltaY); }, { passive: false });");
        builder.AppendLine("    window.addEventListener('wheel', event => { if (!zoomTargetActive) return; event.preventDefault(); applyZoom(event.deltaY); }, { passive: false });");
        builder.AppendLine("    window.addEventListener('keydown', event => { if (!zoomTargetActive) return; const moveStep = Math.max(1.6, sceneExtent * 0.05); if (event.key === 'w' || event.key === 'W') { dolly += moveStep; } else if (event.key === 's' || event.key === 'S') { dolly -= moveStep; } else { return; } event.preventDefault(); render(); });");
        builder.AppendLine("    canvas.addEventListener('dblclick', () => resetView());");
        builder.AppendLine("    render();");
        builder.AppendLine("  </script>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");

        return builder.ToString();
    }

    private static string BuildSurfaceMeshesJson(TerritoryRegionStructureData territory)
    {
        var builder = new StringBuilder();
        builder.Append('[');
        for (var meshIndex = 0; meshIndex < territory.SurfaceMeshes.Count; meshIndex++)
        {
            var mesh = territory.SurfaceMeshes[meshIndex];
            if (meshIndex > 0)
            {
                builder.Append(',');
            }

            builder.Append($"{{\"regionIndex\":{mesh.RegionIndex},\"name\":\"{mesh.Name}\",\"color\":\"{mesh.ColorHex}\",\"vertices\":[");

            for (var vertexIndex = 0; vertexIndex < mesh.Vertices.Count; vertexIndex++)
            {
                var vertex = mesh.Vertices[vertexIndex];
                if (vertexIndex > 0)
                {
                    builder.Append(',');
                }

                builder.Append($"{{\"x\":{vertex.X:0.###},\"y\":{vertex.Y:0.###},\"z\":{vertex.Z:0.###}}}");
            }

            builder.Append("],\"quads\":[");

            for (var quadIndex = 0; quadIndex < mesh.Quads.Count; quadIndex++)
            {
                var quad = mesh.Quads[quadIndex];
                if (quadIndex > 0)
                {
                    builder.Append(',');
                }

                builder.Append($"[{quad.A},{quad.B},{quad.C},{quad.D}]");
            }

            builder.Append("]}");
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string Escape(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}

static class TerritoryHeavyGateNetworkHtmlRenderer
{
    public static string Render(GeneratorConfig config, TerritoryRegionStructureData territory)
    {
        var meshesJson = BuildSurfaceMeshesJson(territory);
        var linksJson = BuildHeavyLinksJson(territory);
        var spanJson = $"{{\"x\":{territory.Span.X:0.###},\"y\":{territory.Span.Y:0.###},\"z\":{territory.Span.Z:0.###}}}";

        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"UTF-8\" />");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />");
        builder.AppendLine($"  <title>{Escape(config.TerritoryName)} Heavy Gate Systems 3D</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    :root { color-scheme: dark; }");
        builder.AppendLine("    body { margin: 0; background: radial-gradient(circle at top, #0b1626, #04070d 65%); color: #e7eef7; font-family: Consolas, 'Courier New', monospace; }");
        builder.AppendLine("    .shell { padding: 24px 28px 18px; }");
        builder.AppendLine("    h1 { margin: 0 0 8px; font-size: 28px; font-weight: 500; }");
        builder.AppendLine("    .meta { color: #8fa6be; font-size: 14px; margin-bottom: 14px; }");
        builder.AppendLine("    .hint { color: #9db4ca; font-size: 13px; margin-bottom: 14px; }");
        builder.AppendLine("    canvas { width: min(1200px, calc(100vw - 56px)); height: min(840px, calc(100vh - 160px)); border: 1px solid #22384d; border-radius: 18px; background: linear-gradient(180deg, rgba(10,20,34,0.92), rgba(3,7,12,0.96)); display: block; box-shadow: 0 18px 48px rgba(0,0,0,0.32); cursor: grab; }");
        builder.AppendLine("    canvas.dragging { cursor: grabbing; }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("  <div class=\"shell\" id=\"viewer-shell\">");
        builder.AppendLine($"    <h1>{Escape(config.TerritoryName)} Heavy Gate Systems 3D</h1>");
        builder.AppendLine($"    <div class=\"meta\">Geography {Escape(config.GeographySeed)} | Heavy pair span {config.MinimumStarDistanceLy * 9.0:0.0}-{config.MinimumStarDistanceLy * 12.0:0.0} ly | {territory.HeavyGateLinks.Count} links</div>");
        builder.AppendLine("    <div class=\"hint\">Left-drag to rotate. Right-drag or Shift-drag to pan. W/S dolly in and out. Scroll to zoom. Double-click to reset view. White stars are heavy-gate systems and lines are inter-region heavy gates.</div>");
        builder.AppendLine("    <canvas id=\"view\" width=\"1200\" height=\"840\"></canvas>");
        builder.AppendLine("  </div>");
        builder.AppendLine("  <script>");
        builder.AppendLine($"    const span = {spanJson};");
        builder.AppendLine($"    const meshes = {meshesJson};");
        builder.AppendLine($"    const links = {linksJson};");
        builder.AppendLine("    const shell = document.getElementById('viewer-shell');");
        builder.AppendLine("    const canvas = document.getElementById('view');");
        builder.AppendLine("    const ctx = canvas.getContext('2d');");
        builder.AppendLine("    let yaw = -0.72;");
        builder.AppendLine("    let pitch = 0.48;");
        builder.AppendLine("    let zoom = 4.2;");
        builder.AppendLine("    let panX = 0;");
        builder.AppendLine("    let panY = 0;");
        builder.AppendLine("    let dolly = 0;");
        builder.AppendLine("    let dragging = false;");
        builder.AppendLine("    let dragMode = 'rotate';");
        builder.AppendLine("    let zoomTargetActive = false;");
        builder.AppendLine("    let lastX = 0;");
        builder.AppendLine("    let lastY = 0;");
        builder.AppendLine("    const sceneExtent = Math.max(span.x, span.y, span.z);");
        builder.AppendLine("    const distanceBase = Math.max(96, sceneExtent * 0.85);");
        builder.AppendLine("    function resetView() { yaw = -0.72; pitch = 0.48; zoom = 4.2; panX = 0; panY = 0; dolly = 0; render(); }");
        builder.AppendLine("    function applyZoom(deltaY) { const factor = Math.exp(deltaY * 0.0030); zoom = Math.max(0.01, Math.min(48.0, zoom * factor)); render(); }");
        builder.AppendLine("    function rotate(point) {");
        builder.AppendLine("      const cy = Math.cos(yaw), sy = Math.sin(yaw);");
        builder.AppendLine("      const cp = Math.cos(pitch), sp = Math.sin(pitch);");
        builder.AppendLine("      const x1 = (point.x * cy) - (point.z * sy);");
        builder.AppendLine("      const z1 = (point.x * sy) + (point.z * cy);");
        builder.AppendLine("      const y2 = (point.y * cp) - (z1 * sp);");
        builder.AppendLine("      const z2 = (point.y * sp) + (z1 * cp);");
        builder.AppendLine("      return { x: x1, y: y2, z: z2 };");
        builder.AppendLine("    }");
        builder.AppendLine("    function project(point) {");
        builder.AppendLine("      const distance = distanceBase + (85 * zoom);");
        builder.AppendLine("      const shiftedZ = Math.min(point.z + dolly, distance - 2.0);");
        builder.AppendLine("      const perspective = distance / (distance - shiftedZ);");
        builder.AppendLine("      return { x: canvas.width / 2 + panX + (point.x * perspective * 3.1), y: canvas.height / 2 + panY + (point.y * perspective * 3.1), z: shiftedZ, scale: perspective };");
        builder.AppendLine("    }");
        builder.AppendLine("    function render() {");
        builder.AppendLine("      ctx.clearRect(0, 0, canvas.width, canvas.height);");
        builder.AppendLine("      ctx.fillStyle = '#07101b';");
        builder.AppendLine("      ctx.fillRect(0, 0, canvas.width, canvas.height);");
        builder.AppendLine("      const surfaces = [];");
        builder.AppendLine("      for (const mesh of meshes) {");
        builder.AppendLine("        const projectedVertices = mesh.vertices.map(rotate).map(project);");
        builder.AppendLine("        for (const quad of mesh.quads) {");
        builder.AppendLine("          const a = projectedVertices[quad[0]];");
        builder.AppendLine("          const b = projectedVertices[quad[1]];");
        builder.AppendLine("          const c = projectedVertices[quad[2]];");
        builder.AppendLine("          const d = projectedVertices[quad[3]];");
        builder.AppendLine("          const depth = (a.z + b.z + c.z + d.z) * 0.25;");
        builder.AppendLine("          surfaces.push({ color: mesh.color, depth, points: [a, b, c, d] });");
        builder.AppendLine("        }");
        builder.AppendLine("      }");
        builder.AppendLine("      surfaces.sort((left, right) => left.depth - right.depth);");
        builder.AppendLine("      for (const surface of surfaces) {");
        builder.AppendLine("        ctx.fillStyle = hexToRgba(surface.color, 0.06);");
        builder.AppendLine("        ctx.strokeStyle = hexToRgba(surface.color, 0.18);");
        builder.AppendLine("        ctx.lineWidth = 0.8;");
        builder.AppendLine("        ctx.beginPath();");
        builder.AppendLine("        ctx.moveTo(surface.points[0].x, surface.points[0].y);");
        builder.AppendLine("        for (let index = 1; index < surface.points.length; index += 1) {");
        builder.AppendLine("          ctx.lineTo(surface.points[index].x, surface.points[index].y);");
        builder.AppendLine("        }");
        builder.AppendLine("        ctx.closePath();");
        builder.AppendLine("        ctx.fill();");
        builder.AppendLine("        ctx.stroke();");
        builder.AppendLine("      }");
        builder.AppendLine("      const projectedLinks = links.map(link => ({");
        builder.AppendLine("        isAdjacent: link.isAdjacent,");
        builder.AppendLine("        start: project(rotate(link.systemA)),");
        builder.AppendLine("        end: project(rotate(link.systemB)),");
        builder.AppendLine("        depth: (rotate(link.systemA).z + rotate(link.systemB).z) * 0.5");
        builder.AppendLine("      })).sort((left, right) => left.depth - right.depth);");
        builder.AppendLine("      for (const link of projectedLinks) {");
        builder.AppendLine("        const glow = link.isAdjacent ? '#f7f7f2' : '#ffe08a';");
        builder.AppendLine("        ctx.strokeStyle = link.isAdjacent ? 'rgba(244,241,222,0.92)' : 'rgba(246,189,96,0.70)';");
        builder.AppendLine("        ctx.lineWidth = 2.2;");
        builder.AppendLine("        ctx.beginPath();");
        builder.AppendLine("        ctx.moveTo(link.start.x, link.start.y);");
        builder.AppendLine("        ctx.lineTo(link.end.x, link.end.y);");
        builder.AppendLine("        ctx.stroke();");
        builder.AppendLine("        drawStar(link.start, glow);");
        builder.AppendLine("        drawStar(link.end, glow);");
        builder.AppendLine("      }");
        builder.AppendLine("    }");
        builder.AppendLine("    function drawStar(point, glow) {");
        builder.AppendLine("      ctx.fillStyle = hexToRgba(glow, 0.16);");
        builder.AppendLine("      ctx.beginPath();");
        builder.AppendLine("      ctx.arc(point.x, point.y, 5.6, 0, Math.PI * 2);");
        builder.AppendLine("      ctx.fill();");
        builder.AppendLine("      ctx.fillStyle = '#ffffff';");
        builder.AppendLine("      ctx.strokeStyle = glow;");
        builder.AppendLine("      ctx.lineWidth = 0.9;");
        builder.AppendLine("      ctx.beginPath();");
        builder.AppendLine("      ctx.arc(point.x, point.y, 2.2, 0, Math.PI * 2);");
        builder.AppendLine("      ctx.fill();");
        builder.AppendLine("      ctx.stroke();");
        builder.AppendLine("    }");
        builder.AppendLine("    function hexToRgba(hex, alpha) {");
        builder.AppendLine("      const normalized = hex.replace('#', '');");
        builder.AppendLine("      const r = parseInt(normalized.slice(0, 2), 16);");
        builder.AppendLine("      const g = parseInt(normalized.slice(2, 4), 16);");
        builder.AppendLine("      const b = parseInt(normalized.slice(4, 6), 16);");
        builder.AppendLine("      return `rgba(${r}, ${g}, ${b}, ${alpha})`;");
        builder.AppendLine("    }");
        builder.AppendLine("    canvas.addEventListener('pointerdown', event => { dragging = true; dragMode = (event.button === 2 || event.shiftKey) ? 'pan' : 'rotate'; lastX = event.clientX; lastY = event.clientY; canvas.classList.add('dragging'); });");
        builder.AppendLine("    canvas.addEventListener('contextmenu', event => event.preventDefault());");
        builder.AppendLine("    shell.addEventListener('pointerenter', () => { zoomTargetActive = true; });");
        builder.AppendLine("    shell.addEventListener('pointerleave', () => { zoomTargetActive = false; });");
        builder.AppendLine("    window.addEventListener('pointerup', () => { dragging = false; canvas.classList.remove('dragging'); });");
        builder.AppendLine("    window.addEventListener('pointermove', event => { if (!dragging) return; const dx = event.clientX - lastX; const dy = event.clientY - lastY; lastX = event.clientX; lastY = event.clientY; if (dragMode === 'pan') { panX += dx; panY += dy; } else { yaw += dx * 0.008; pitch = Math.max(-1.35, Math.min(1.35, pitch + dy * 0.008)); } render(); });");
        builder.AppendLine("    shell.addEventListener('wheel', event => { event.preventDefault(); applyZoom(event.deltaY); }, { passive: false });");
        builder.AppendLine("    window.addEventListener('wheel', event => { if (!zoomTargetActive) return; event.preventDefault(); applyZoom(event.deltaY); }, { passive: false });");
        builder.AppendLine("    window.addEventListener('keydown', event => { if (!zoomTargetActive) return; const moveStep = Math.max(1.6, sceneExtent * 0.05); if (event.key === 'w' || event.key === 'W') { dolly += moveStep; } else if (event.key === 's' || event.key === 'S') { dolly -= moveStep; } else { return; } event.preventDefault(); render(); });");
        builder.AppendLine("    canvas.addEventListener('dblclick', () => resetView());");
        builder.AppendLine("    render();");
        builder.AppendLine("  </script>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");

        return builder.ToString();
    }

    private static string BuildSurfaceMeshesJson(TerritoryRegionStructureData territory)
    {
        var builder = new StringBuilder();
        builder.Append('[');
        for (var meshIndex = 0; meshIndex < territory.SurfaceMeshes.Count; meshIndex++)
        {
            var mesh = territory.SurfaceMeshes[meshIndex];
            if (meshIndex > 0)
            {
                builder.Append(',');
            }

            builder.Append($"{{\"color\":\"{mesh.ColorHex}\",\"vertices\":[");
            for (var vertexIndex = 0; vertexIndex < mesh.Vertices.Count; vertexIndex++)
            {
                var vertex = mesh.Vertices[vertexIndex];
                if (vertexIndex > 0)
                {
                    builder.Append(',');
                }

                builder.Append($"{{\"x\":{vertex.X:0.###},\"y\":{vertex.Y:0.###},\"z\":{vertex.Z:0.###}}}");
            }

            builder.Append("],\"quads\":[");
            for (var quadIndex = 0; quadIndex < mesh.Quads.Count; quadIndex++)
            {
                var quad = mesh.Quads[quadIndex];
                if (quadIndex > 0)
                {
                    builder.Append(',');
                }

                builder.Append($"[{quad.A},{quad.B},{quad.C},{quad.D}]");
            }

            builder.Append("]}");
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string BuildHeavyLinksJson(TerritoryRegionStructureData territory)
    {
        var builder = new StringBuilder();
        builder.Append('[');
        for (var index = 0; index < territory.HeavyGateLinks.Count; index++)
        {
            var link = territory.HeavyGateLinks[index];
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append($"{{\"systemA\":{{\"x\":{link.SystemA.X:0.###},\"y\":{link.SystemA.Y:0.###},\"z\":{link.SystemA.Z:0.###}}},\"systemB\":{{\"x\":{link.SystemB.X:0.###},\"y\":{link.SystemB.Y:0.###},\"z\":{link.SystemB.Z:0.###}}},\"isAdjacent\":{link.IsAdjacent.ToString().ToLowerInvariant()}}}");
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string Escape(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}

static class TerritoryStarMapHtmlRenderer
{
    public static string Render(GeneratorConfig config, TerritoryRegionStructureData territory, TerritoryNetworkSnapshot networkSnapshot)
    {
        var systemsJson = BuildSystemsJson(territory, networkSnapshot);
        var systemCount = networkSnapshot.Systems.Count;

        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"UTF-8\" />");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />");
        builder.AppendLine($"  <title>{Escape(config.TerritoryName)} Territory Star Map 3D</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    :root { color-scheme: dark; }");
        builder.AppendLine("    body { margin: 0; background: radial-gradient(circle at top, #09131f, #03060b 68%); color: #e7eef7; font-family: Consolas, 'Courier New', monospace; }");
        builder.AppendLine("    .shell { padding: 24px 28px 18px; }");
        builder.AppendLine("    h1 { margin: 0 0 8px; font-size: 28px; font-weight: 500; }");
        builder.AppendLine("    .meta { color: #8fa6be; font-size: 14px; margin-bottom: 14px; }");
        builder.AppendLine("    .hint { color: #9db4ca; font-size: 13px; margin-bottom: 14px; }");
        builder.AppendLine("    canvas { width: min(1200px, calc(100vw - 56px)); height: min(840px, calc(100vh - 160px)); border: 1px solid #22384d; border-radius: 18px; background: radial-gradient(circle at 50% 45%, rgba(13,24,38,0.98), rgba(3,7,12,0.98) 70%); display: block; box-shadow: 0 18px 48px rgba(0,0,0,0.32); cursor: grab; }");
        builder.AppendLine("    canvas.dragging { cursor: grabbing; }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("  <div class=\"shell\" id=\"viewer-shell\">");
        builder.AppendLine($"    <h1>{Escape(config.TerritoryName)} Territory Star Map</h1>");
        builder.AppendLine($"    <div class=\"meta\">Geography {Escape(config.GeographySeed)} | Regions {territory.Regions.Count} | Systems {systemCount} | True positions, colored by region</div>");
        builder.AppendLine("    <div class=\"hint\">Left-drag to rotate. Right-drag or Shift-drag to pan. W/S dolly in and out. Scroll to zoom. Double-click to reset view. This diagnostic renders the full territory star field to scale with no gate paths.</div>");
        builder.AppendLine("    <canvas id=\"view\" width=\"1200\" height=\"840\"></canvas>");
        builder.AppendLine("  </div>");
        builder.AppendLine("  <script>");
        builder.AppendLine($"    const systems = {systemsJson};");
        builder.AppendLine("    const shell = document.getElementById('viewer-shell');");
        builder.AppendLine("    const canvas = document.getElementById('view');");
        builder.AppendLine("    const ctx = canvas.getContext('2d');");
        builder.AppendLine("    let yaw = -0.72;");
        builder.AppendLine("    let pitch = 0.48;");
        builder.AppendLine("    let zoom = 4.0;");
        builder.AppendLine("    let panX = 0;");
        builder.AppendLine("    let panY = 0;");
        builder.AppendLine("    let dolly = 0;");
        builder.AppendLine("    let dragging = false;");
        builder.AppendLine("    let dragMode = 'rotate';");
        builder.AppendLine("    let zoomTargetActive = false;");
        builder.AppendLine("    let lastX = 0;");
        builder.AppendLine("    let lastY = 0;");
        builder.AppendLine("    const bounds = systems.reduce((state, item) => ({");
        builder.AppendLine("      minX: Math.min(state.minX, item.position.x),");
        builder.AppendLine("      maxX: Math.max(state.maxX, item.position.x),");
        builder.AppendLine("      minY: Math.min(state.minY, item.position.y),");
        builder.AppendLine("      maxY: Math.max(state.maxY, item.position.y),");
        builder.AppendLine("      minZ: Math.min(state.minZ, item.position.z),");
        builder.AppendLine("      maxZ: Math.max(state.maxZ, item.position.z)");
        builder.AppendLine("    }), { minX: Infinity, maxX: -Infinity, minY: Infinity, maxY: -Infinity, minZ: Infinity, maxZ: -Infinity });");
        builder.AppendLine("    const sceneCenter = { x: (bounds.minX + bounds.maxX) * 0.5, y: (bounds.minY + bounds.maxY) * 0.5, z: (bounds.minZ + bounds.maxZ) * 0.5 };");
        builder.AppendLine("    const sceneExtent = Math.max(1, (bounds.maxX - bounds.minX) * 0.5, (bounds.maxY - bounds.minY) * 0.5, (bounds.maxZ - bounds.minZ) * 0.5);");
        builder.AppendLine("    const baseScale = Math.max(2.2, 700 / Math.max(1, sceneExtent * 2));");
        builder.AppendLine("    const distanceBase = Math.max(42, sceneExtent * 0.92);");
        builder.AppendLine("    function resetView() { yaw = -0.72; pitch = 0.48; zoom = 4.0; panX = 0; panY = 0; dolly = 0; render(); }");
        builder.AppendLine("    function applyZoom(deltaY) { const factor = Math.exp(deltaY * 0.0030); zoom = Math.max(0.01, Math.min(56.0, zoom * factor)); render(); }");
        builder.AppendLine("    function centerPoint(system) { return { x: system.position.x - sceneCenter.x, y: system.position.y - sceneCenter.y, z: system.position.z - sceneCenter.z, color: system.color, radius: system.radius }; }");
        builder.AppendLine("    function rotate(point) {");
        builder.AppendLine("      const cy = Math.cos(yaw), sy = Math.sin(yaw);");
        builder.AppendLine("      const cp = Math.cos(pitch), sp = Math.sin(pitch);");
        builder.AppendLine("      const x1 = (point.x * cy) - (point.z * sy);");
        builder.AppendLine("      const z1 = (point.x * sy) + (point.z * cy);");
        builder.AppendLine("      const y2 = (point.y * cp) - (z1 * sp);");
        builder.AppendLine("      const z2 = (point.y * sp) + (z1 * cp);");
        builder.AppendLine("      return { x: x1, y: y2, z: z2, color: point.color, radius: point.radius };");
        builder.AppendLine("    }");
        builder.AppendLine("    function project(point) {");
        builder.AppendLine("      const distance = distanceBase + (76 * zoom);");
        builder.AppendLine("      const shiftedZ = Math.min(point.z + dolly, distance - 2.0);");
        builder.AppendLine("      const perspective = distance / (distance - shiftedZ);");
        builder.AppendLine("      return { x: canvas.width / 2 + panX + (point.x * perspective * baseScale), y: canvas.height / 2 + panY + (point.y * perspective * baseScale), z: shiftedZ, scale: perspective, color: point.color, radius: point.radius };");
        builder.AppendLine("    }");
        builder.AppendLine("    function render() {");
        builder.AppendLine("      ctx.clearRect(0, 0, canvas.width, canvas.height);");
        builder.AppendLine("      ctx.fillStyle = '#04080d';");
        builder.AppendLine("      ctx.fillRect(0, 0, canvas.width, canvas.height);");
        builder.AppendLine("      const projectedSystems = systems.map(item => project(rotate(centerPoint(item)))).sort((left, right) => left.z - right.z);");
        builder.AppendLine("      for (const point of projectedSystems) {");
        builder.AppendLine("        const glowRadius = Math.max(1.8, point.radius * point.scale * 2.8);");
        builder.AppendLine("        const coreRadius = Math.max(1.0, point.radius * point.scale * 1.15);");
        builder.AppendLine("        ctx.fillStyle = hexToRgba(point.color, 0.16);");
        builder.AppendLine("        ctx.beginPath();");
        builder.AppendLine("        ctx.arc(point.x, point.y, glowRadius, 0, Math.PI * 2);");
        builder.AppendLine("        ctx.fill();");
        builder.AppendLine("        ctx.fillStyle = point.color;");
        builder.AppendLine("        ctx.strokeStyle = 'rgba(255,255,255,0.22)';");
        builder.AppendLine("        ctx.lineWidth = 0.65;");
        builder.AppendLine("        ctx.beginPath();");
        builder.AppendLine("        ctx.arc(point.x, point.y, coreRadius, 0, Math.PI * 2);");
        builder.AppendLine("        ctx.fill();");
        builder.AppendLine("        ctx.stroke();");
        builder.AppendLine("      }");
        builder.AppendLine("    }");
        builder.AppendLine("    function hexToRgba(hex, alpha) {");
        builder.AppendLine("      const normalized = hex.replace('#', '');");
        builder.AppendLine("      const r = parseInt(normalized.slice(0, 2), 16);");
        builder.AppendLine("      const g = parseInt(normalized.slice(2, 4), 16);");
        builder.AppendLine("      const b = parseInt(normalized.slice(4, 6), 16);");
        builder.AppendLine("      return `rgba(${r}, ${g}, ${b}, ${alpha})`;");
        builder.AppendLine("    }");
        builder.AppendLine("    canvas.addEventListener('pointerdown', event => { dragging = true; dragMode = (event.button === 2 || event.shiftKey) ? 'pan' : 'rotate'; lastX = event.clientX; lastY = event.clientY; canvas.classList.add('dragging'); });");
        builder.AppendLine("    canvas.addEventListener('contextmenu', event => event.preventDefault());");
        builder.AppendLine("    shell.addEventListener('pointerenter', () => { zoomTargetActive = true; });");
        builder.AppendLine("    shell.addEventListener('pointerleave', () => { zoomTargetActive = false; });");
        builder.AppendLine("    window.addEventListener('pointerup', () => { dragging = false; canvas.classList.remove('dragging'); });");
        builder.AppendLine("    window.addEventListener('pointermove', event => { if (!dragging) return; const dx = event.clientX - lastX; const dy = event.clientY - lastY; lastX = event.clientX; lastY = event.clientY; if (dragMode === 'pan') { panX += dx; panY += dy; } else { yaw += dx * 0.008; pitch = Math.max(-1.35, Math.min(1.35, pitch + dy * 0.008)); } render(); });");
        builder.AppendLine("    shell.addEventListener('wheel', event => { event.preventDefault(); applyZoom(event.deltaY); }, { passive: false });");
        builder.AppendLine("    window.addEventListener('wheel', event => { if (!zoomTargetActive) return; event.preventDefault(); applyZoom(event.deltaY); }, { passive: false });");
        builder.AppendLine("    window.addEventListener('keydown', event => { if (!zoomTargetActive) return; const moveStep = Math.max(1.6, sceneExtent * 0.05); if (event.key === 'w' || event.key === 'W') { dolly += moveStep; } else if (event.key === 's' || event.key === 'S') { dolly -= moveStep; } else { return; } event.preventDefault(); render(); });");
        builder.AppendLine("    canvas.addEventListener('dblclick', () => resetView());");
        builder.AppendLine("    render();");
        builder.AppendLine("  </script>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");

        return builder.ToString();
    }

    private static string BuildSystemsJson(TerritoryRegionStructureData territory, TerritoryNetworkSnapshot networkSnapshot)
    {
        var regionColorByIndex = territory.Regions.ToDictionary(region => region.Index, region => region.ColorHex);
        var builder = new StringBuilder();
        builder.Append('[');
        for (var index = 0; index < networkSnapshot.Systems.Count; index++)
        {
            var system = networkSnapshot.Systems[index];
            if (index > 0)
            {
                builder.Append(',');
            }

            var colorHex = regionColorByIndex[system.RegionIndex];
            var radius = 1.2;
            builder.Append($"{{\"position\":{{\"x\":{system.Position.X:0.###},\"y\":{system.Position.Y:0.###},\"z\":{system.Position.Z:0.###}}},\"color\":\"{colorHex}\",\"radius\":{radius:0.###}}}");
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string Escape(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}

static class RegionSectorStructureHtmlRenderer
{
    private const int SurfaceLatitudeBands = 10;
    private const int SurfaceLongitudeBands = 20;

    public static string Render(GeneratorConfig config, RegionSectorSet regionSectors, TerritoryNetworkSnapshot networkSnapshot)
    {
        var sectorMeshesJson = BuildSectorMeshesJson(regionSectors);
        var sectorNucleiJson = BuildSectorNucleiJson(regionSectors);
        var sectorLinksJson = BuildSectorLinksJson(regionSectors, networkSnapshot);
        var sectorSystemsJson = BuildSectorSystemsJson(regionSectors, networkSnapshot);
        var systemById = networkSnapshot.Systems.ToDictionary(system => system.Id);
        var regionSystemCount = networkSnapshot.Systems.Count(system => system.RegionIndex == regionSectors.Region.Index);
        var regionLinkCount = networkSnapshot.Links.Count(link => systemById[link.SystemAId].RegionIndex == regionSectors.Region.Index && systemById[link.SystemBId].RegionIndex == regionSectors.Region.Index);

        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"UTF-8\" />");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />");
        builder.AppendLine($"  <title>{Escape(config.TerritoryName)} {Escape(regionSectors.Region.Name)} Sector 3D</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    :root { color-scheme: dark; }");
        builder.AppendLine("    body { margin: 0; background: radial-gradient(circle at top, #0b1626, #04070d 65%); color: #e7eef7; font-family: Consolas, 'Courier New', monospace; }");
        builder.AppendLine("    .shell { padding: 24px 28px 18px; }");
        builder.AppendLine("    h1 { margin: 0 0 8px; font-size: 28px; font-weight: 500; }");
        builder.AppendLine("    .meta { color: #8fa6be; font-size: 14px; margin-bottom: 14px; }");
        builder.AppendLine("    .hint { color: #9db4ca; font-size: 13px; margin-bottom: 14px; }");
        builder.AppendLine("    canvas { width: min(1200px, calc(100vw - 56px)); height: min(840px, calc(100vh - 160px)); border: 1px solid #22384d; border-radius: 18px; background: linear-gradient(180deg, rgba(10,20,34,0.92), rgba(3,7,12,0.96)); display: block; box-shadow: 0 18px 48px rgba(0,0,0,0.32); cursor: grab; }");
        builder.AppendLine("    canvas.dragging { cursor: grabbing; }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("  <div class=\"shell\" id=\"viewer-shell\">");
        builder.AppendLine($"    <h1>{Escape(regionSectors.Region.Name)} Sector Structure 3D</h1>");
        builder.AppendLine($"    <div class=\"meta\">{Escape(config.TerritoryName)} | Parent Region {Escape(regionSectors.Region.Name)} | Sector Cells {regionSectors.Sectors.Count} | Systems {regionSystemCount} | Links {regionLinkCount}</div>");
        builder.AppendLine("    <div class=\"hint\">Left-drag to rotate. Right-drag or Shift-drag to pan. W/S dolly in and out. Scroll to zoom. Double-click to reset view. This diagnostic renders the region's sector cells and full local star-gate network.</div>");
        builder.AppendLine("    <canvas id=\"view\" width=\"1200\" height=\"840\"></canvas>");
        builder.AppendLine("  </div>");
        builder.AppendLine("  <script>");
        builder.AppendLine($"    const meshes = {sectorMeshesJson};");
        builder.AppendLine($"    const nuclei = {sectorNucleiJson};");
        builder.AppendLine($"    const links = {sectorLinksJson};");
        builder.AppendLine($"    const systems = {sectorSystemsJson};");
        builder.AppendLine("    const shell = document.getElementById('viewer-shell');");
        builder.AppendLine("    const canvas = document.getElementById('view');");
        builder.AppendLine("    const ctx = canvas.getContext('2d');");
        builder.AppendLine("    let yaw = -0.72;");
        builder.AppendLine("    let pitch = 0.48;");
        builder.AppendLine("    let zoom = 4.2;");
        builder.AppendLine("    let panX = 0;");
        builder.AppendLine("    let panY = 0;");
        builder.AppendLine("    let dolly = 0;");
        builder.AppendLine("    let dragging = false;");
        builder.AppendLine("    let dragMode = 'rotate';");
        builder.AppendLine("    let zoomTargetActive = false;");
        builder.AppendLine("    let lastX = 0;");
        builder.AppendLine("    let lastY = 0;");
        builder.AppendLine("    const scenePoints = [...systems.map(item => item.position), ...nuclei.map(item => item.position), ...meshes.flatMap(mesh => mesh.vertices)];");
        builder.AppendLine("    const bounds = scenePoints.reduce((state, point) => ({");
        builder.AppendLine("      minX: Math.min(state.minX, point.x),");
        builder.AppendLine("      maxX: Math.max(state.maxX, point.x),");
        builder.AppendLine("      minY: Math.min(state.minY, point.y),");
        builder.AppendLine("      maxY: Math.max(state.maxY, point.y),");
        builder.AppendLine("      minZ: Math.min(state.minZ, point.z),");
        builder.AppendLine("      maxZ: Math.max(state.maxZ, point.z)");
        builder.AppendLine("    }), { minX: Infinity, maxX: -Infinity, minY: Infinity, maxY: -Infinity, minZ: Infinity, maxZ: -Infinity });");
        builder.AppendLine("    const sceneCenter = {");
        builder.AppendLine("      x: (bounds.minX + bounds.maxX) * 0.5,");
        builder.AppendLine("      y: (bounds.minY + bounds.maxY) * 0.5,");
        builder.AppendLine("      z: (bounds.minZ + bounds.maxZ) * 0.5");
        builder.AppendLine("    };");
        builder.AppendLine("    const sceneExtent = Math.max(1, (bounds.maxX - bounds.minX) * 0.5, (bounds.maxY - bounds.minY) * 0.5, (bounds.maxZ - bounds.minZ) * 0.5);");
        builder.AppendLine("    const baseScale = Math.max(4.8, 560 / Math.max(1, sceneExtent * 2));");
        builder.AppendLine("    const distanceBase = Math.max(34, sceneExtent * 0.9);");
        builder.AppendLine("    function resetView() { yaw = -0.72; pitch = 0.48; zoom = 4.2; panX = 0; panY = 0; dolly = 0; render(); }");
        builder.AppendLine("    function applyZoom(deltaY) { const factor = Math.exp(deltaY * 0.0030); zoom = Math.max(0.01, Math.min(48.0, zoom * factor)); render(); }");
        builder.AppendLine("    function centerPoint(point) { return { x: point.x - sceneCenter.x, y: point.y - sceneCenter.y, z: point.z - sceneCenter.z }; }");
        builder.AppendLine("    function rotate(point) {");
        builder.AppendLine("      const cy = Math.cos(yaw), sy = Math.sin(yaw);");
        builder.AppendLine("      const cp = Math.cos(pitch), sp = Math.sin(pitch);");
        builder.AppendLine("      const x1 = (point.x * cy) - (point.z * sy);");
        builder.AppendLine("      const z1 = (point.x * sy) + (point.z * cy);");
        builder.AppendLine("      const y2 = (point.y * cp) - (z1 * sp);");
        builder.AppendLine("      const z2 = (point.y * sp) + (z1 * cp);");
        builder.AppendLine("      return { x: x1, y: y2, z: z2 };");
        builder.AppendLine("    }");
        builder.AppendLine("    function project(point) {");
        builder.AppendLine("      const distance = distanceBase + (62 * zoom);");
        builder.AppendLine("      const shiftedZ = Math.min(point.z + dolly, distance - 2.0);");
        builder.AppendLine("      const perspective = distance / (distance - shiftedZ);");
        builder.AppendLine("      return { x: canvas.width / 2 + panX + (point.x * perspective * baseScale), y: canvas.height / 2 + panY + (point.y * perspective * baseScale), z: shiftedZ, scale: perspective };");
        builder.AppendLine("    }");
        builder.AppendLine("    function render() {");
        builder.AppendLine("      ctx.clearRect(0, 0, canvas.width, canvas.height);");
        builder.AppendLine("      ctx.fillStyle = '#07101b';");
        builder.AppendLine("      ctx.fillRect(0, 0, canvas.width, canvas.height);");
        builder.AppendLine("      const surfaces = [];");
        builder.AppendLine("      for (const mesh of meshes) {");
        builder.AppendLine("        const projectedVertices = mesh.vertices.map(centerPoint).map(rotate).map(project);");
        builder.AppendLine("        for (const quad of mesh.quads) {");
        builder.AppendLine("          const a = projectedVertices[quad[0]];");
        builder.AppendLine("          const b = projectedVertices[quad[1]];");
        builder.AppendLine("          const c = projectedVertices[quad[2]];");
        builder.AppendLine("          const d = projectedVertices[quad[3]];");
        builder.AppendLine("          const depth = (a.z + b.z + c.z + d.z) * 0.25;");
        builder.AppendLine("          surfaces.push({ color: mesh.color, depth, points: [a, b, c, d] });");
        builder.AppendLine("        }");
        builder.AppendLine("      }");
        builder.AppendLine("      surfaces.sort((left, right) => left.depth - right.depth);");
        builder.AppendLine("      for (const surface of surfaces) {");
        builder.AppendLine("        ctx.fillStyle = hexToRgba(surface.color, 0.18);");
        builder.AppendLine("        ctx.strokeStyle = hexToRgba(surface.color, 0.72);");
        builder.AppendLine("        ctx.lineWidth = 1.0;");
        builder.AppendLine("        ctx.beginPath();");
        builder.AppendLine("        ctx.moveTo(surface.points[0].x, surface.points[0].y);");
        builder.AppendLine("        for (let index = 1; index < surface.points.length; index += 1) {");
        builder.AppendLine("          ctx.lineTo(surface.points[index].x, surface.points[index].y);");
        builder.AppendLine("        }");
        builder.AppendLine("        ctx.closePath();");
        builder.AppendLine("        ctx.fill();");
        builder.AppendLine("        ctx.stroke();");
        builder.AppendLine("      }");
        builder.AppendLine("      const projectedLinks = links.map(link => ({");
        builder.AppendLine("        start: project(rotate(centerPoint(link.start))),");
        builder.AppendLine("        end: project(rotate(centerPoint(link.end))),");
        builder.AppendLine("        gateType: link.gateType,");
        builder.AppendLine("        depth: (rotate(centerPoint(link.start)).z + rotate(centerPoint(link.end)).z) * 0.5");
        builder.AppendLine("      })).sort((left, right) => left.depth - right.depth);");
        builder.AppendLine("      for (const link of projectedLinks) {");
        builder.AppendLine("        ctx.strokeStyle = gateStroke(link.gateType);");
        builder.AppendLine("        ctx.lineWidth = gateWidth(link.gateType);");
        builder.AppendLine("        ctx.beginPath();");
        builder.AppendLine("        ctx.moveTo(link.start.x, link.start.y);");
        builder.AppendLine("        ctx.lineTo(link.end.x, link.end.y);");
        builder.AppendLine("        ctx.stroke();");
        builder.AppendLine("      }");
        builder.AppendLine("      const projectedSystems = systems.map(item => ({ item, projected: project(rotate(centerPoint(item.position))) })).sort((left, right) => left.projected.z - right.projected.z);");
        builder.AppendLine("      for (const entry of projectedSystems) {");
        builder.AppendLine("        drawNetworkSystem(entry.projected, entry.item);");
        builder.AppendLine("      }");
        builder.AppendLine("      const projectedNuclei = nuclei.map(item => ({ projected: project(rotate(centerPoint(item.position))), label: item.label, color: item.color })).sort((left, right) => left.projected.z - right.projected.z);");
        builder.AppendLine("      for (const item of projectedNuclei) {");
        builder.AppendLine("        ctx.fillStyle = item.color;");
        builder.AppendLine("        ctx.strokeStyle = 'rgba(255,255,255,0.92)';");
        builder.AppendLine("        ctx.lineWidth = 1.0;");
        builder.AppendLine("        ctx.beginPath();");
        builder.AppendLine("        ctx.arc(item.projected.x, item.projected.y, Math.max(3.4, item.projected.scale * 3.6), 0, Math.PI * 2);");
        builder.AppendLine("        ctx.fill();");
        builder.AppendLine("        ctx.stroke();");
        builder.AppendLine("        ctx.fillStyle = 'rgba(235,242,248,0.95)';");
        builder.AppendLine("        ctx.font = '11px Consolas';");
        builder.AppendLine("        ctx.fillText(item.label, item.projected.x + 6, item.projected.y - 6);");
        builder.AppendLine("      }");
        builder.AppendLine("    }");
        builder.AppendLine("    function hexToRgba(hex, alpha) {");
        builder.AppendLine("      const normalized = hex.replace('#', '');");
        builder.AppendLine("      const r = parseInt(normalized.slice(0, 2), 16);");
        builder.AppendLine("      const g = parseInt(normalized.slice(2, 4), 16);");
        builder.AppendLine("      const b = parseInt(normalized.slice(4, 6), 16);");
        builder.AppendLine("      return `rgba(${r}, ${g}, ${b}, ${alpha})`;");
        builder.AppendLine("    }");
        builder.AppendLine("    function gateStroke(gateType) {");
        builder.AppendLine("      return gateType === 'Heavy' ? 'rgba(246, 189, 96, 0.82)' : gateType === 'Medium' ? 'rgba(158, 216, 255, 0.84)' : 'rgba(183, 240, 199, 0.60)';");
        builder.AppendLine("    }");
        builder.AppendLine("    function gateWidth(gateType) {");
        builder.AppendLine("      return gateType === 'Heavy' ? 2.4 : gateType === 'Medium' ? 2.0 : 1.2;");
        builder.AppendLine("    }");
        builder.AppendLine("    function drawNetworkSystem(point, item) {");
        builder.AppendLine("      const radius = Math.max(item.radius, point.scale * item.radius);");
        builder.AppendLine("      ctx.fillStyle = item.glow;");
        builder.AppendLine("      ctx.beginPath();");
        builder.AppendLine("      ctx.arc(point.x, point.y, radius * 2.0, 0, Math.PI * 2);");
        builder.AppendLine("      ctx.fill();");
        builder.AppendLine("      ctx.fillStyle = item.fill;");
        builder.AppendLine("      ctx.strokeStyle = item.stroke;");
        builder.AppendLine("      ctx.lineWidth = 1.0;");
        builder.AppendLine("      ctx.beginPath();");
        builder.AppendLine("      ctx.arc(point.x, point.y, radius, 0, Math.PI * 2);");
        builder.AppendLine("      ctx.fill();");
        builder.AppendLine("      ctx.stroke();");
        builder.AppendLine("      if (item.label) {");
        builder.AppendLine("        ctx.fillStyle = 'rgba(235,242,248,0.92)';");
        builder.AppendLine("        ctx.font = '10px Consolas';");
        builder.AppendLine("        ctx.fillText(item.label, point.x + 5, point.y - 5);");
        builder.AppendLine("      }");
        builder.AppendLine("    }");
        builder.AppendLine("    canvas.addEventListener('pointerdown', event => { dragging = true; dragMode = (event.button === 2 || event.shiftKey) ? 'pan' : 'rotate'; lastX = event.clientX; lastY = event.clientY; canvas.classList.add('dragging'); });");
        builder.AppendLine("    canvas.addEventListener('contextmenu', event => event.preventDefault());");
        builder.AppendLine("    shell.addEventListener('pointerenter', () => { zoomTargetActive = true; });");
        builder.AppendLine("    shell.addEventListener('pointerleave', () => { zoomTargetActive = false; });");
        builder.AppendLine("    window.addEventListener('pointerup', () => { dragging = false; canvas.classList.remove('dragging'); });");
        builder.AppendLine("    window.addEventListener('pointermove', event => { if (!dragging) return; const dx = event.clientX - lastX; const dy = event.clientY - lastY; lastX = event.clientX; lastY = event.clientY; if (dragMode === 'pan') { panX += dx; panY += dy; } else { yaw += dx * 0.008; pitch = Math.max(-1.35, Math.min(1.35, pitch + dy * 0.008)); } render(); });");
        builder.AppendLine("    shell.addEventListener('wheel', event => { event.preventDefault(); applyZoom(event.deltaY); }, { passive: false });");
        builder.AppendLine("    window.addEventListener('wheel', event => { if (!zoomTargetActive) return; event.preventDefault(); applyZoom(event.deltaY); }, { passive: false });");
        builder.AppendLine("    window.addEventListener('keydown', event => { if (!zoomTargetActive) return; const moveStep = Math.max(1.6, sceneExtent * 0.05); if (event.key === 'w' || event.key === 'W') { dolly += moveStep; } else if (event.key === 's' || event.key === 'S') { dolly -= moveStep; } else { return; } event.preventDefault(); render(); });");
        builder.AppendLine("    canvas.addEventListener('dblclick', () => resetView());");
        builder.AppendLine("    render();");
        builder.AppendLine("  </script>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");

        return builder.ToString();
    }

    private static string BuildSectorMeshesJson(RegionSectorSet regionSectors)
    {
        var meshes = BuildSectorSurfaceMeshes(regionSectors);
        var builder = new StringBuilder();
        builder.Append('[');

        for (var meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
        {
            var mesh = meshes[meshIndex];
            if (meshIndex > 0)
            {
                builder.Append(',');
            }

            builder.Append($"{{\"color\":\"{mesh.ColorHex}\",\"vertices\":[");
            for (var vertexIndex = 0; vertexIndex < mesh.Vertices.Count; vertexIndex++)
            {
                var vertex = mesh.Vertices[vertexIndex];
                if (vertexIndex > 0)
                {
                    builder.Append(',');
                }

                builder.Append($"{{\"x\":{vertex.X:0.###},\"y\":{vertex.Y:0.###},\"z\":{vertex.Z:0.###}}}");
            }

            builder.Append("],\"quads\":[");
            for (var quadIndex = 0; quadIndex < mesh.Quads.Count; quadIndex++)
            {
                var quad = mesh.Quads[quadIndex];
                if (quadIndex > 0)
                {
                    builder.Append(',');
                }

                builder.Append($"[{quad.A},{quad.B},{quad.C},{quad.D}]");
            }

            builder.Append("]}");
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string BuildSectorNucleiJson(RegionSectorSet regionSectors)
    {
        var builder = new StringBuilder();
        builder.Append('[');
        for (var index = 0; index < regionSectors.Sectors.Count; index++)
        {
            var sector = regionSectors.Sectors[index];
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append($"{{\"label\":\"{sector.Name}\",\"color\":\"{sector.ColorHex}\",\"position\":{{\"x\":{sector.Nucleus.X:0.###},\"y\":{sector.Nucleus.Y:0.###},\"z\":{sector.Nucleus.Z:0.###}}}}}");
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string BuildSectorLinksJson(RegionSectorSet regionSectors, TerritoryNetworkSnapshot networkSnapshot)
    {
        var systemById = networkSnapshot.Systems.ToDictionary(system => system.Id);
        var regionLinks = networkSnapshot.Links
            .Where(link => systemById[link.SystemAId].RegionIndex == regionSectors.Region.Index && systemById[link.SystemBId].RegionIndex == regionSectors.Region.Index)
            .ToList();
        var builder = new StringBuilder();
        builder.Append('[');
        for (var index = 0; index < regionLinks.Count; index++)
        {
            var link = regionLinks[index];
            var start = systemById[link.SystemAId].Position;
            var end = systemById[link.SystemBId].Position;
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append($"{{\"start\":{{\"x\":{start.X:0.###},\"y\":{start.Y:0.###},\"z\":{start.Z:0.###}}},\"end\":{{\"x\":{end.X:0.###},\"y\":{end.Y:0.###},\"z\":{end.Z:0.###}}},\"gateType\":\"{link.GateType}\"}}");
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string BuildSectorSystemsJson(RegionSectorSet regionSectors, TerritoryNetworkSnapshot networkSnapshot)
    {
        var sectorColorByIndex = regionSectors.Sectors.ToDictionary(sector => sector.Index, sector => sector.ColorHex);
        var systems = networkSnapshot.Systems
            .Where(system => system.RegionIndex == regionSectors.Region.Index)
            .OrderBy(system => system.SectorIndex)
            .ThenBy(system => system.Address)
            .ToList();
        var builder = new StringBuilder();
        builder.Append('[');
        for (var index = 0; index < systems.Count; index++)
        {
            var system = systems[index];
            if (index > 0)
            {
                builder.Append(',');
            }

            var hasHeavy = system.Gates.Any(gate => gate.GateType == "Heavy");
            var hasMedium = system.Gates.Any(gate => gate.GateType == "Medium");
            var radius = hasHeavy ? 3.2 : hasMedium ? 2.7 : system.SourceType.Contains("Connector", StringComparison.OrdinalIgnoreCase) ? 1.4 : 1.9;
            var stroke = hasHeavy ? "rgba(255, 209, 102, 0.92)" : hasMedium ? "rgba(143, 211, 255, 0.90)" : "rgba(255,255,255,0.88)";
            var glow = hasHeavy ? "rgba(255, 209, 102, 0.12)" : hasMedium ? "rgba(143, 211, 255, 0.12)" : "rgba(255,255,255,0.08)";
            var label = hasHeavy || hasMedium ? system.Name : string.Empty;
            builder.Append($"{{\"position\":{{\"x\":{system.Position.X:0.###},\"y\":{system.Position.Y:0.###},\"z\":{system.Position.Z:0.###}}},\"fill\":\"{sectorColorByIndex[system.SectorIndex]}\",\"stroke\":\"{stroke}\",\"glow\":\"{glow}\",\"radius\":{radius:0.0},\"label\":\"{label}\"}}");
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static IReadOnlyList<RegionSurfaceMesh> BuildSectorSurfaceMeshes(RegionSectorSet regionSectors)
    {
        var meshes = new List<RegionSurfaceMesh>(regionSectors.Sectors.Count);
        foreach (var sector in regionSectors.Sectors)
        {
            var ownedPoints = regionSectors.OwnedSamples
                .Where(sample => sample.OwnerIndex == sector.Index)
                .Select(sample => sample.Position)
                .ToList();

            if (ownedPoints.Count == 0)
            {
                ownedPoints.Add(sector.Nucleus);
            }

            var vertices = new List<Point3>((SurfaceLatitudeBands + 1) * SurfaceLongitudeBands);
            var quads = new List<MeshQuad>(SurfaceLatitudeBands * SurfaceLongitudeBands);
            for (var latitudeIndex = 0; latitudeIndex <= SurfaceLatitudeBands; latitudeIndex++)
            {
                var theta = Math.PI * latitudeIndex / SurfaceLatitudeBands;
                var sinTheta = Math.Sin(theta);
                var cosTheta = Math.Cos(theta);

                for (var longitudeIndex = 0; longitudeIndex < SurfaceLongitudeBands; longitudeIndex++)
                {
                    var phi = (Math.PI * 2.0 * longitudeIndex) / SurfaceLongitudeBands;
                    var direction = Normalize(new Point3(
                        sinTheta * Math.Cos(phi),
                        cosTheta,
                        sinTheta * Math.Sin(phi)));
                    vertices.Add(FindSectorSurfacePoint(sector.Nucleus, ownedPoints, direction));
                }
            }

            for (var latitudeIndex = 0; latitudeIndex < SurfaceLatitudeBands; latitudeIndex++)
            {
                var rowStart = latitudeIndex * SurfaceLongitudeBands;
                var nextRowStart = (latitudeIndex + 1) * SurfaceLongitudeBands;
                for (var longitudeIndex = 0; longitudeIndex < SurfaceLongitudeBands; longitudeIndex++)
                {
                    var nextLongitude = (longitudeIndex + 1) % SurfaceLongitudeBands;
                    quads.Add(new MeshQuad(rowStart + longitudeIndex, rowStart + nextLongitude, nextRowStart + nextLongitude, nextRowStart + longitudeIndex));
                }
            }

            meshes.Add(new RegionSurfaceMesh(sector.Index, sector.Name, sector.ColorHex, vertices, quads));
        }

        return meshes;
    }

    private static Point3 FindSectorSurfacePoint(Point3 nucleus, IReadOnlyList<Point3> ownedPoints, Point3 direction)
    {
        var bestPoint = nucleus;
        var bestScore = double.MinValue;

        foreach (var point in ownedPoints)
        {
            var offset = Subtract(point, nucleus);
            var score = Dot(offset, direction);
            if (score > bestScore)
            {
                bestScore = score;
                bestPoint = point;
            }
        }

        return bestPoint;
    }

    private static Point3 Subtract(Point3 left, Point3 right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    private static double Dot(Point3 left, Point3 right) => (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    private static Point3 Normalize(Point3 point)
    {
        var length = Math.Sqrt((point.X * point.X) + (point.Y * point.Y) + (point.Z * point.Z));
        if (length <= 0.0)
        {
            return new Point3(0, 1, 0);
        }

        return new Point3(point.X / length, point.Y / length, point.Z / length);
    }

    private static string Escape(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}

static class TerritoryStarGateMapHtmlRenderer
{
    public static string Render(GeneratorConfig config, TerritoryRegionStructureData territory, TerritoryNetworkSnapshot networkSnapshot)
    {
        var systemsJson = BuildSystemsJson(territory, networkSnapshot);
        var linksJson = BuildLinksJson(networkSnapshot);
        var systemCount = networkSnapshot.Systems.Count;
        var linkCount = networkSnapshot.Links.Count;

        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"UTF-8\" />");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />");
        builder.AppendLine($"  <title>{Escape(config.TerritoryName)} Territory Star Gate Map 3D</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    :root { color-scheme: dark; }");
        builder.AppendLine("    body { margin: 0; background: radial-gradient(circle at top, #09131f, #03060b 68%); color: #e7eef7; font-family: Consolas, 'Courier New', monospace; }");
        builder.AppendLine("    .shell { padding: 24px 28px 18px; }");
        builder.AppendLine("    h1 { margin: 0 0 8px; font-size: 28px; font-weight: 500; }");
        builder.AppendLine("    .meta { color: #8fa6be; font-size: 14px; margin-bottom: 14px; }");
        builder.AppendLine("    .hint { color: #9db4ca; font-size: 13px; margin-bottom: 14px; }");
        builder.AppendLine("    canvas { width: min(1200px, calc(100vw - 56px)); height: min(840px, calc(100vh - 160px)); border: 1px solid #22384d; border-radius: 18px; background: radial-gradient(circle at 50% 45%, rgba(13,24,38,0.98), rgba(3,7,12,0.98) 70%); display: block; box-shadow: 0 18px 48px rgba(0,0,0,0.32); cursor: grab; }");
        builder.AppendLine("    canvas.dragging { cursor: grabbing; }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("  <div class=\"shell\" id=\"viewer-shell\">");
        builder.AppendLine($"    <h1>{Escape(config.TerritoryName)} Territory Star Gate Map</h1>");
        builder.AppendLine($"    <div class=\"meta\">Geography {Escape(config.GeographySeed)} | Regions {territory.Regions.Count} | Systems {systemCount} | Links {linkCount}</div>");
        builder.AppendLine("    <div class=\"hint\">Left-drag to rotate. Right-drag or Shift-drag to pan. W/S dolly in and out. Scroll to zoom. Double-click to reset view. This diagnostic renders the full territory star field and star-gate network to scale.</div>");
        builder.AppendLine("    <canvas id=\"view\" width=\"1200\" height=\"840\"></canvas>");
        builder.AppendLine("  </div>");
        builder.AppendLine("  <script>");
        builder.AppendLine($"    const systems = {systemsJson};");
        builder.AppendLine($"    const links = {linksJson};");
        builder.AppendLine("    const shell = document.getElementById('viewer-shell');");
        builder.AppendLine("    const canvas = document.getElementById('view');");
        builder.AppendLine("    const ctx = canvas.getContext('2d');");
        builder.AppendLine("    let yaw = -0.72;");
        builder.AppendLine("    let pitch = 0.48;");
        builder.AppendLine("    let zoom = 4.0;");
        builder.AppendLine("    let panX = 0;");
        builder.AppendLine("    let panY = 0;");
        builder.AppendLine("    let dolly = 0;");
        builder.AppendLine("    let dragging = false;");
        builder.AppendLine("    let dragMode = 'rotate';");
        builder.AppendLine("    let zoomTargetActive = false;");
        builder.AppendLine("    let lastX = 0;");
        builder.AppendLine("    let lastY = 0;");
        builder.AppendLine("    const bounds = systems.reduce((state, item) => ({");
        builder.AppendLine("      minX: Math.min(state.minX, item.position.x),");
        builder.AppendLine("      maxX: Math.max(state.maxX, item.position.x),");
        builder.AppendLine("      minY: Math.min(state.minY, item.position.y),");
        builder.AppendLine("      maxY: Math.max(state.maxY, item.position.y),");
        builder.AppendLine("      minZ: Math.min(state.minZ, item.position.z),");
        builder.AppendLine("      maxZ: Math.max(state.maxZ, item.position.z)");
        builder.AppendLine("    }), { minX: Infinity, maxX: -Infinity, minY: Infinity, maxY: -Infinity, minZ: Infinity, maxZ: -Infinity });");
        builder.AppendLine("    const sceneCenter = { x: (bounds.minX + bounds.maxX) * 0.5, y: (bounds.minY + bounds.maxY) * 0.5, z: (bounds.minZ + bounds.maxZ) * 0.5 };");
        builder.AppendLine("    const sceneExtent = Math.max(1, (bounds.maxX - bounds.minX) * 0.5, (bounds.maxY - bounds.minY) * 0.5, (bounds.maxZ - bounds.minZ) * 0.5);");
        builder.AppendLine("    const baseScale = Math.max(2.2, 700 / Math.max(1, sceneExtent * 2));");
        builder.AppendLine("    const distanceBase = Math.max(42, sceneExtent * 0.92);");
        builder.AppendLine("    function resetView() { yaw = -0.72; pitch = 0.48; zoom = 4.0; panX = 0; panY = 0; dolly = 0; render(); }");
        builder.AppendLine("    function applyZoom(deltaY) { const factor = Math.exp(deltaY * 0.0030); zoom = Math.max(0.01, Math.min(56.0, zoom * factor)); render(); }");
        builder.AppendLine("    function centerSystem(system) { return { x: system.position.x - sceneCenter.x, y: system.position.y - sceneCenter.y, z: system.position.z - sceneCenter.z, color: system.color, radius: system.radius }; }");
        builder.AppendLine("    function centerLinkPoint(point) { return { x: point.x - sceneCenter.x, y: point.y - sceneCenter.y, z: point.z - sceneCenter.z }; }");
        builder.AppendLine("    function rotate(point) {");
        builder.AppendLine("      const cy = Math.cos(yaw), sy = Math.sin(yaw);");
        builder.AppendLine("      const cp = Math.cos(pitch), sp = Math.sin(pitch);");
        builder.AppendLine("      const x1 = (point.x * cy) - (point.z * sy);");
        builder.AppendLine("      const z1 = (point.x * sy) + (point.z * cy);");
        builder.AppendLine("      const y2 = (point.y * cp) - (z1 * sp);");
        builder.AppendLine("      const z2 = (point.y * sp) + (z1 * cp);");
        builder.AppendLine("      return { x: x1, y: y2, z: z2, color: point.color, radius: point.radius };");
        builder.AppendLine("    }");
        builder.AppendLine("    function project(point) {");
        builder.AppendLine("      const distance = distanceBase + (76 * zoom);");
        builder.AppendLine("      const shiftedZ = Math.min(point.z + dolly, distance - 2.0);");
        builder.AppendLine("      const perspective = distance / (distance - shiftedZ);");
        builder.AppendLine("      return { x: canvas.width / 2 + panX + (point.x * perspective * baseScale), y: canvas.height / 2 + panY + (point.y * perspective * baseScale), z: shiftedZ, scale: perspective, color: point.color, radius: point.radius };");
        builder.AppendLine("    }");
        builder.AppendLine("    function render() {");
        builder.AppendLine("      ctx.clearRect(0, 0, canvas.width, canvas.height);");
        builder.AppendLine("      ctx.fillStyle = '#04080d';");
        builder.AppendLine("      ctx.fillRect(0, 0, canvas.width, canvas.height);");
        builder.AppendLine("      const projectedLinks = links.map(link => {");
        builder.AppendLine("        const start = project(rotate(centerLinkPoint(link.start)));");
        builder.AppendLine("        const end = project(rotate(centerLinkPoint(link.end)));");
        builder.AppendLine("        return { start, end, gateType: link.gateType, depth: (start.z + end.z) * 0.5 };\n      }).sort((left, right) => left.depth - right.depth);");
        builder.AppendLine("      for (const link of projectedLinks) {");
        builder.AppendLine("        ctx.strokeStyle = gateStroke(link.gateType);");
        builder.AppendLine("        ctx.lineWidth = gateWidth(link.gateType);");
        builder.AppendLine("        ctx.beginPath();");
        builder.AppendLine("        ctx.moveTo(link.start.x, link.start.y);");
        builder.AppendLine("        ctx.lineTo(link.end.x, link.end.y);");
        builder.AppendLine("        ctx.stroke();");
        builder.AppendLine("      }");
        builder.AppendLine("      const projectedSystems = systems.map(item => project(rotate(centerSystem(item)))).sort((left, right) => left.z - right.z);");
        builder.AppendLine("      for (const point of projectedSystems) {");
        builder.AppendLine("        const glowRadius = Math.max(1.8, point.radius * point.scale * 2.8);");
        builder.AppendLine("        const coreRadius = Math.max(1.0, point.radius * point.scale * 1.15);");
        builder.AppendLine("        ctx.fillStyle = hexToRgba(point.color, 0.14);");
        builder.AppendLine("        ctx.beginPath();");
        builder.AppendLine("        ctx.arc(point.x, point.y, glowRadius, 0, Math.PI * 2);");
        builder.AppendLine("        ctx.fill();");
        builder.AppendLine("        ctx.fillStyle = point.color;");
        builder.AppendLine("        ctx.strokeStyle = 'rgba(255,255,255,0.20)';");
        builder.AppendLine("        ctx.lineWidth = 0.6;");
        builder.AppendLine("        ctx.beginPath();");
        builder.AppendLine("        ctx.arc(point.x, point.y, coreRadius, 0, Math.PI * 2);");
        builder.AppendLine("        ctx.fill();");
        builder.AppendLine("        ctx.stroke();");
        builder.AppendLine("      }");
        builder.AppendLine("    }");
        builder.AppendLine("    function gateStroke(gateType) {");
        builder.AppendLine("      return gateType === 'Heavy' ? 'rgba(246, 169, 84, 0.34)' : gateType === 'Medium' ? 'rgba(132, 214, 160, 0.24)' : 'rgba(214, 232, 255, 0.18)';");
        builder.AppendLine("    }");
        builder.AppendLine("    function gateWidth(gateType) {");
        builder.AppendLine("      return gateType === 'Heavy' ? 4.4 : gateType === 'Medium' ? 3.2 : 1.8;");
        builder.AppendLine("    }");
        builder.AppendLine("    function hexToRgba(hex, alpha) {");
        builder.AppendLine("      const normalized = hex.replace('#', '');");
        builder.AppendLine("      const r = parseInt(normalized.slice(0, 2), 16);");
        builder.AppendLine("      const g = parseInt(normalized.slice(2, 4), 16);");
        builder.AppendLine("      const b = parseInt(normalized.slice(4, 6), 16);");
        builder.AppendLine("      return `rgba(${r}, ${g}, ${b}, ${alpha})`;");
        builder.AppendLine("    }");
        builder.AppendLine("    canvas.addEventListener('pointerdown', event => { dragging = true; dragMode = (event.button === 2 || event.shiftKey) ? 'pan' : 'rotate'; lastX = event.clientX; lastY = event.clientY; canvas.classList.add('dragging'); });");
        builder.AppendLine("    canvas.addEventListener('contextmenu', event => event.preventDefault());");
        builder.AppendLine("    shell.addEventListener('pointerenter', () => { zoomTargetActive = true; });");
        builder.AppendLine("    shell.addEventListener('pointerleave', () => { zoomTargetActive = false; });");
        builder.AppendLine("    window.addEventListener('pointerup', () => { dragging = false; canvas.classList.remove('dragging'); });");
        builder.AppendLine("    window.addEventListener('pointermove', event => { if (!dragging) return; const dx = event.clientX - lastX; const dy = event.clientY - lastY; lastX = event.clientX; lastY = event.clientY; if (dragMode === 'pan') { panX += dx; panY += dy; } else { yaw += dx * 0.008; pitch = Math.max(-1.35, Math.min(1.35, pitch + dy * 0.008)); } render(); });");
        builder.AppendLine("    shell.addEventListener('wheel', event => { event.preventDefault(); applyZoom(event.deltaY); }, { passive: false });");
        builder.AppendLine("    window.addEventListener('wheel', event => { if (!zoomTargetActive) return; event.preventDefault(); applyZoom(event.deltaY); }, { passive: false });");
        builder.AppendLine("    window.addEventListener('keydown', event => { if (!zoomTargetActive) return; const moveStep = Math.max(1.6, sceneExtent * 0.05); if (event.key === 'w' || event.key === 'W') { dolly += moveStep; } else if (event.key === 's' || event.key === 'S') { dolly -= moveStep; } else { return; } event.preventDefault(); render(); });");
        builder.AppendLine("    canvas.addEventListener('dblclick', () => resetView());");
        builder.AppendLine("    render();");
        builder.AppendLine("  </script>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");

        return builder.ToString();
    }

    private static string BuildSystemsJson(TerritoryRegionStructureData territory, TerritoryNetworkSnapshot networkSnapshot)
    {
        var regionColorByIndex = territory.Regions.ToDictionary(region => region.Index, region => region.ColorHex);
        var builder = new StringBuilder();
        builder.Append('[');
        for (var index = 0; index < networkSnapshot.Systems.Count; index++)
        {
            var system = networkSnapshot.Systems[index];
            if (index > 0)
            {
                builder.Append(',');
            }

            var colorHex = regionColorByIndex[system.RegionIndex];
            builder.Append($"{{\"position\":{{\"x\":{system.Position.X:0.###},\"y\":{system.Position.Y:0.###},\"z\":{system.Position.Z:0.###}}},\"color\":\"{colorHex}\",\"radius\":1.2}}");
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string BuildLinksJson(TerritoryNetworkSnapshot networkSnapshot)
    {
        var systemById = networkSnapshot.Systems.ToDictionary(system => system.Id);
        var builder = new StringBuilder();
        builder.Append('[');
        for (var index = 0; index < networkSnapshot.Links.Count; index++)
        {
            var link = networkSnapshot.Links[index];
            if (index > 0)
            {
                builder.Append(',');
            }

            var start = systemById[link.SystemAId].Position;
            var end = systemById[link.SystemBId].Position;
            builder.Append($"{{\"start\":{{\"x\":{start.X:0.###},\"y\":{start.Y:0.###},\"z\":{start.Z:0.###}}},\"end\":{{\"x\":{end.X:0.###},\"y\":{end.Y:0.###},\"z\":{end.Z:0.###}}},\"gateType\":\"{link.GateType}\"}}");
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string Escape(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}

static class SectorNavigationMapSvgRenderer
{
    public static string Render(GeneratorConfig config, RegionSectorSet regionSectors, SectorCell sector, TerritoryNetworkSnapshot networkSnapshot)
    {
        const int width = 1400;
        const int height = 980;
        const double padding = 28.0;
        const double bottomPadding = 28.0;
        const double topOffset = 112.0;

        var systemById = networkSnapshot.Systems.ToDictionary(system => system.Id);
        var internalSystems = networkSnapshot.Systems
            .Where(system => system.RegionIndex == regionSectors.Region.Index && system.SectorIndex == sector.Index)
            .OrderBy(system => system.Address)
            .ToList();
        var internalIds = internalSystems.Select(system => system.Id).ToHashSet();
        var relevantLinks = networkSnapshot.Links
            .Where(link => internalIds.Contains(link.SystemAId) || internalIds.Contains(link.SystemBId))
            .ToList();
        var exitSystemIds = relevantLinks
            .SelectMany(link => new[] { link.SystemAId, link.SystemBId })
            .Where(id => !internalIds.Contains(id))
            .Distinct()
            .ToList();
        var externalSystems = exitSystemIds.Select(id => systemById[id]).OrderBy(system => system.Address).ToList();
        var topDownPoints = regionSectors.OwnedSamples
            .Where(sample => sample.OwnerIndex == sector.Index)
            .Select(sample => new Point2(sample.Position.X, -sample.Position.Y))
            .ToList();
        if (topDownPoints.Count == 0)
        {
            topDownPoints.Add(new Point2(sector.Nucleus.X, -sector.Nucleus.Y));
        }

        var layoutPositions = BuildNavLayout(internalSystems, externalSystems, relevantLinks);
        var usableWidth = width - (padding * 2.0);
        var usableHeight = height - topOffset - bottomPadding;

        Point2 Map(Point2 point) => new(
            padding + (point.X * usableWidth),
            topOffset + (point.Y * usableHeight));

        var mappedPositions = layoutPositions.ToDictionary(item => item.Key, item => Map(item.Value));
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
        builder.AppendLine("  <defs>");
        builder.AppendLine("    <linearGradient id=\"bg\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\">");
        builder.AppendLine("      <stop offset=\"0%\" stop-color=\"#07111f\" />");
        builder.AppendLine("      <stop offset=\"100%\" stop-color=\"#02060b\" />");
        builder.AppendLine("    </linearGradient>");
        builder.AppendLine("  </defs>");
        builder.AppendLine("  <rect width=\"100%\" height=\"100%\" fill=\"url(#bg)\" />");
        builder.AppendLine($"  <text x=\"40\" y=\"54\" fill=\"#e6eef8\" font-size=\"28\" font-family=\"Consolas, 'Courier New', monospace\">NAV {Escape(sector.Name)}</text>");
        builder.AppendLine($"  <text x=\"40\" y=\"82\" fill=\"#88a3bf\" font-size=\"15\" font-family=\"Consolas, 'Courier New', monospace\">{Escape(config.TerritoryName)} | In-Sector Systems {internalSystems.Count} | Exit Systems {externalSystems.Count} | Links {relevantLinks.Count}</text>");

        foreach (var link in relevantLinks)
        {
            var left = systemById[link.SystemAId];
            var right = systemById[link.SystemBId];
            var leftMapped = mappedPositions[left.Id];
            var rightMapped = mappedPositions[right.Id];
            var dashed = !(internalIds.Contains(link.SystemAId) && internalIds.Contains(link.SystemBId));
            builder.AppendLine($"  <line x1=\"{leftMapped.X:0.00}\" y1=\"{leftMapped.Y:0.00}\" x2=\"{rightMapped.X:0.00}\" y2=\"{rightMapped.Y:0.00}\" stroke=\"{GetNavGateStroke(link.GateType)}\" stroke-width=\"{GetNavGateWidth(link.GateType):0.00}\" stroke-opacity=\"{(dashed ? 0.74 : 0.88):0.00}\"{(dashed ? " stroke-dasharray=\"7 5\"" : string.Empty)} />");
        }

        foreach (var system in internalSystems)
        {
            AppendNavSystem(builder, mappedPositions[system.Id], system, sector.ColorHex, true);
        }

        foreach (var system in externalSystems)
        {
            AppendNavSystem(builder, mappedPositions[system.Id], system, "#7f8ea3", false);
        }

        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    private static void AppendNavSystem(StringBuilder builder, Point2 point, NetworkSystemNode system, string fill, bool isInternal)
    {
        var hasHeavy = system.Gates.Any(gate => gate.GateType == "Heavy");
        var hasMedium = system.Gates.Any(gate => gate.GateType == "Medium");
        var label = isInternal ? system.Name : system.Address;
        var boxFill = isInternal ? "#1f2831" : fill;
        var textFill = isInternal ? "#f5f7fa" : "#f3f7fb";
        var stroke = hasHeavy ? "#d9a441" : hasMedium ? "#78bddd" : (isInternal ? "#aeb9c5" : "#f3f7fb");
        var glow = hasHeavy ? "#d9a441" : hasMedium ? "#78bddd" : (isInternal ? "#607080" : fill);
        var fontSize = 10.0;
        var charWidth = 6.15;
        var horizontalPadding = isInternal ? 8.0 : 9.0;
        var boxWidth = Math.Max(24.0, (label.Length * charWidth) + (horizontalPadding * 2.0));
        var boxHeight = hasHeavy ? 20.0 : hasMedium ? 18.5 : 17.0;
        var cornerRadius = hasHeavy ? 6.0 : 5.0;
        var glowInset = hasHeavy ? 2.6 : hasMedium ? 2.2 : 1.8;
        var left = point.X - (boxWidth / 2.0);
        var top = point.Y - (boxHeight / 2.0);

        builder.AppendLine($"  <rect x=\"{left - glowInset:0.00}\" y=\"{top - glowInset:0.00}\" width=\"{boxWidth + (glowInset * 2.0):0.00}\" height=\"{boxHeight + (glowInset * 2.0):0.00}\" rx=\"{cornerRadius + 1.5:0.00}\" fill=\"{glow}\" fill-opacity=\"0.10\" />");
        builder.AppendLine($"  <rect x=\"{left:0.00}\" y=\"{top:0.00}\" width=\"{boxWidth:0.00}\" height=\"{boxHeight:0.00}\" rx=\"{cornerRadius:0.00}\" fill=\"{boxFill}\" fill-opacity=\"0.92\" stroke=\"{stroke}\" stroke-width=\"{(hasHeavy ? 1.4 : hasMedium ? 1.2 : 1.0):0.00}\" />");
        builder.AppendLine($"  <text x=\"{point.X:0.00}\" y=\"{point.Y + 3.25:0.00}\" fill=\"{textFill}\" font-size=\"{fontSize:0.0}\" text-anchor=\"middle\" font-family=\"Consolas, 'Courier New', monospace\">{Escape(label)}</text>");
    }

    private static string GetNavGateStroke(string gateType)
    {
        return gateType.Equals("Heavy", StringComparison.OrdinalIgnoreCase)
            ? "#f6bd60"
            : gateType.Equals("Medium", StringComparison.OrdinalIgnoreCase)
                ? "#9ed8ff"
                : "#b7f0c7";
    }

    private static double GetNavGateWidth(string gateType)
    {
        return gateType.Equals("Heavy", StringComparison.OrdinalIgnoreCase)
            ? 2.6
            : gateType.Equals("Medium", StringComparison.OrdinalIgnoreCase)
                ? 2.1
                : 1.4;
    }

    private static Dictionary<int, Point2> BuildNavLayout(
        IReadOnlyList<NetworkSystemNode> internalSystems,
        IReadOnlyList<NetworkSystemNode> externalSystems,
        IReadOnlyList<NetworkLinkEdge> relevantLinks)
    {
        var positions = InitializeInternalNavPositions(internalSystems);
        RelaxInternalNavPositions(positions, internalSystems, relevantLinks);
        NormalizeInternalNavPositions(positions, internalSystems);
        OptimizeInternalNavPositions(positions, internalSystems, relevantLinks);
        PlaceExternalNavPositions(positions, internalSystems, externalSystems, relevantLinks);
        FitNavLayoutToViewport(positions);
        return positions;
    }

    private static Dictionary<int, Point2> InitializeInternalNavPositions(IReadOnlyList<NetworkSystemNode> internalSystems)
    {
        var positions = new Dictionary<int, Point2>(internalSystems.Count);
        if (internalSystems.Count == 0)
        {
            return positions;
        }

        if (internalSystems.Count == 1)
        {
            positions[internalSystems[0].Id] = new Point2(0.5, 0.5);
            return positions;
        }

        var rawPoints = internalSystems
            .Select(system => new Point2(system.Position.X, -system.Position.Y))
            .ToList();
        var minX = rawPoints.Min(point => point.X);
        var maxX = rawPoints.Max(point => point.X);
        var minY = rawPoints.Min(point => point.Y);
        var maxY = rawPoints.Max(point => point.Y);
        var spanX = maxX - minX;
        var spanY = maxY - minY;
        var useCircleSeed = spanX < 0.001 && spanY < 0.001;

        for (var index = 0; index < internalSystems.Count; index++)
        {
            var system = internalSystems[index];
            var angle = (Math.PI * 2.0 * index) / internalSystems.Count;
            var ringSeed = new Point2(0.5 + (Math.Cos(angle) * 0.28), 0.5 + (Math.Sin(angle) * 0.28));
            if (useCircleSeed)
            {
                positions[system.Id] = ringSeed;
                continue;
            }

            var raw = rawPoints[index];
            var normalized = new Point2(
                0.24 + (((raw.X - minX) / Math.Max(0.001, spanX)) * 0.52),
                0.24 + (((raw.Y - minY) / Math.Max(0.001, spanY)) * 0.52));
            positions[system.Id] = Lerp(normalized, ringSeed, 0.40);
        }

        return positions;
    }

    private static void RelaxInternalNavPositions(
        Dictionary<int, Point2> positions,
        IReadOnlyList<NetworkSystemNode> internalSystems,
        IReadOnlyList<NetworkLinkEdge> relevantLinks)
    {
        if (internalSystems.Count <= 1)
        {
            return;
        }

        var internalIds = internalSystems.Select(system => system.Id).ToHashSet();
        var links = relevantLinks
            .Where(link => internalIds.Contains(link.SystemAId) && internalIds.Contains(link.SystemBId))
            .ToList();
        var displacements = internalSystems.ToDictionary(system => system.Id, _ => new Point2(0.0, 0.0));
        var iterations = Math.Clamp(140 + (internalSystems.Count * 3), 160, 280);

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            foreach (var system in internalSystems)
            {
                displacements[system.Id] = new Point2(0.0, 0.0);
            }

            for (var leftIndex = 0; leftIndex < internalSystems.Count; leftIndex++)
            {
                var left = internalSystems[leftIndex];
                for (var rightIndex = leftIndex + 1; rightIndex < internalSystems.Count; rightIndex++)
                {
                    var right = internalSystems[rightIndex];
                    var delta = Subtract(positions[left.Id], positions[right.Id]);
                    var distance = Math.Max(0.001, Magnitude(delta));
                    var direction = Scale(delta, 1.0 / distance);
                    var force = 0.0012 / (distance * distance);
                    var vector = Scale(direction, force);
                    displacements[left.Id] = Add(displacements[left.Id], vector);
                    displacements[right.Id] = Subtract(displacements[right.Id], vector);
                }
            }

            foreach (var link in links)
            {
                var start = positions[link.SystemAId];
                var end = positions[link.SystemBId];
                var delta = Subtract(end, start);
                var distance = Math.Max(0.001, Magnitude(delta));
                var direction = Scale(delta, 1.0 / distance);
                var target = GetNavSpringLength(link.GateType);
                var force = (distance - target) * 0.16;
                var vector = Scale(direction, force);
                displacements[link.SystemAId] = Add(displacements[link.SystemAId], vector);
                displacements[link.SystemBId] = Subtract(displacements[link.SystemBId], vector);
            }

            var temperature = 0.045 - ((0.035 * iteration) / Math.Max(1, iterations - 1));
            foreach (var system in internalSystems)
            {
                var centered = Scale(Subtract(new Point2(0.5, 0.5), positions[system.Id]), 0.040);
                var next = Add(positions[system.Id], Scale(Add(displacements[system.Id], centered), temperature));
                positions[system.Id] = new Point2(Clamp(next.X, 0.02, 0.98), Clamp(next.Y, 0.02, 0.98));
            }
        }
    }

    private static void NormalizeInternalNavPositions(
        Dictionary<int, Point2> positions,
        IReadOnlyList<NetworkSystemNode> internalSystems)
    {
        if (internalSystems.Count == 0)
        {
            return;
        }

        var points = internalSystems.Select(system => positions[system.Id]).ToList();
        var center = new Point2(points.Average(point => point.X), points.Average(point => point.Y));
        var radius = Math.Max(0.001, points.Max(point => Magnitude(Subtract(point, center))));
        const double targetRadius = 0.36;

        foreach (var system in internalSystems)
        {
            var point = positions[system.Id];
            var offset = Subtract(point, center);
            var scaled = Add(new Point2(0.5, 0.5), Scale(offset, targetRadius / radius));
            positions[system.Id] = new Point2(Clamp(scaled.X, 0.08, 0.92), Clamp(scaled.Y, 0.08, 0.92));
        }
    }

    private static void OptimizeInternalNavPositions(
        Dictionary<int, Point2> positions,
        IReadOnlyList<NetworkSystemNode> internalSystems,
        IReadOnlyList<NetworkLinkEdge> relevantLinks)
    {
        if (internalSystems.Count <= 2)
        {
            return;
        }

        var internalIds = internalSystems.Select(system => system.Id).ToHashSet();
        var links = relevantLinks
            .Where(link => internalIds.Contains(link.SystemAId) && internalIds.Contains(link.SystemBId))
            .ToList();
        var neighbors = internalSystems.ToDictionary(
            system => system.Id,
            _ => new HashSet<int>());

        foreach (var link in links)
        {
            neighbors[link.SystemAId].Add(link.SystemBId);
            neighbors[link.SystemBId].Add(link.SystemAId);
        }

        for (var pass = 0; pass < 10; pass++)
        {
            var moved = false;
            foreach (var system in internalSystems.OrderByDescending(system => neighbors[system.Id].Count))
            {
                var current = positions[system.Id];
                var best = current;
                var bestScore = ScoreNavLayout(positions, links);

                foreach (var candidate in BuildNavCandidates(system.Id, current, positions, neighbors))
                {
                    positions[system.Id] = candidate;
                    var candidateScore = ScoreNavLayout(positions, links);
                    if (candidateScore < bestScore)
                    {
                        bestScore = candidateScore;
                        best = candidate;
                    }
                }

                positions[system.Id] = best;
                moved |= best.X != current.X || best.Y != current.Y;
            }

            if (!moved)
            {
                break;
            }
        }
    }

    private static IEnumerable<Point2> BuildNavCandidates(
        int systemId,
        Point2 current,
        IReadOnlyDictionary<int, Point2> positions,
        IReadOnlyDictionary<int, HashSet<int>> neighbors)
    {
        yield return current;

        var neighborIds = neighbors[systemId].ToList();
        if (neighborIds.Count > 0)
        {
            var barycenter = new Point2(
                neighborIds.Average(id => positions[id].X),
                neighborIds.Average(id => positions[id].Y));
            yield return ClampNavPoint(Lerp(current, barycenter, 0.70));
            yield return ClampNavPoint(Lerp(current, barycenter, 0.45));

            foreach (var neighborId in neighborIds)
            {
                var towardNeighbor = Lerp(current, positions[neighborId], 0.35);
                yield return ClampNavPoint(towardNeighbor);
            }
        }

        var offsets = new[]
        {
            new Point2(-0.035, 0.0),
            new Point2(0.035, 0.0),
            new Point2(0.0, -0.035),
            new Point2(0.0, 0.035),
            new Point2(-0.025, -0.025),
            new Point2(0.025, -0.025),
            new Point2(-0.025, 0.025),
            new Point2(0.025, 0.025)
        };

        foreach (var offset in offsets)
        {
            yield return ClampNavPoint(Add(current, offset));
        }
    }

    private static double ScoreNavLayout(
        IReadOnlyDictionary<int, Point2> positions,
        IReadOnlyList<NetworkLinkEdge> links)
    {
        double score = 0.0;

        for (var index = 0; index < links.Count; index++)
        {
            var link = links[index];
            var start = positions[link.SystemAId];
            var end = positions[link.SystemBId];
            var length = Magnitude(Subtract(end, start));
            score += length * length * 8.0;

            for (var compareIndex = index + 1; compareIndex < links.Count; compareIndex++)
            {
                var other = links[compareIndex];
                if (link.SystemAId == other.SystemAId || link.SystemAId == other.SystemBId || link.SystemBId == other.SystemAId || link.SystemBId == other.SystemBId)
                {
                    continue;
                }

                if (SegmentsIntersect(start, end, positions[other.SystemAId], positions[other.SystemBId]))
                {
                    score += 20.0;
                }
            }
        }

        var positionList = positions.ToList();
        for (var leftIndex = 0; leftIndex < positionList.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < positionList.Count; rightIndex++)
            {
                var distance = Magnitude(Subtract(positionList[leftIndex].Value, positionList[rightIndex].Value));
                if (distance < 0.055)
                {
                    var crowding = 0.055 - distance;
                    score += crowding * crowding * 80.0;
                }
            }
        }

        return score;
    }

    private static void FitNavLayoutToViewport(Dictionary<int, Point2> positions)
    {
        if (positions.Count == 0)
        {
            return;
        }

        var points = positions.Values.ToList();
        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);
        var spanX = Math.Max(0.001, maxX - minX);
        var spanY = Math.Max(0.001, maxY - minY);

        const double targetMinX = 0.05;
        const double targetMaxX = 0.92;
        const double targetMinY = 0.05;
        const double targetMaxY = 0.95;
        var targetWidth = targetMaxX - targetMinX;
        var targetHeight = targetMaxY - targetMinY;

        foreach (var entry in positions.ToList())
        {
            var point = entry.Value;
            var normalizedX = (point.X - minX) / spanX;
            var normalizedY = (point.Y - minY) / spanY;
            positions[entry.Key] = new Point2(
                targetMinX + (normalizedX * targetWidth),
                targetMinY + (normalizedY * targetHeight));
        }
    }

    private static Point2 ClampNavPoint(Point2 point) => new(Clamp(point.X, 0.08, 0.92), Clamp(point.Y, 0.08, 0.92));

    private static bool SegmentsIntersect(Point2 a1, Point2 a2, Point2 b1, Point2 b2)
    {
        var o1 = Orientation(a1, a2, b1);
        var o2 = Orientation(a1, a2, b2);
        var o3 = Orientation(b1, b2, a1);
        var o4 = Orientation(b1, b2, a2);
        return (o1 * o2 < 0.0) && (o3 * o4 < 0.0);
    }

    private static double Orientation(Point2 a, Point2 b, Point2 c)
    {
        return ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
    }

    private static void PlaceExternalNavPositions(
        Dictionary<int, Point2> positions,
        IReadOnlyList<NetworkSystemNode> internalSystems,
        IReadOnlyList<NetworkSystemNode> externalSystems,
        IReadOnlyList<NetworkLinkEdge> relevantLinks)
    {
        if (externalSystems.Count == 0)
        {
            return;
        }

        var internalIds = internalSystems.Select(system => system.Id).ToHashSet();
        var clusterCenter = new Point2(
            internalSystems.Average(system => positions[system.Id].X),
            internalSystems.Average(system => positions[system.Id].Y));
        var sectorBoundary = ExpandHull(
            BuildTopDownHull(internalSystems.Select(system => positions[system.Id]).ToList()),
            0.12);
        var externalPlacements = new List<(int SystemId, Point2 Anchor, Point2 Direction)>();

        foreach (var system in externalSystems)
        {
            var neighborIds = relevantLinks
                .Where(link => link.SystemAId == system.Id || link.SystemBId == system.Id)
                .Select(link => link.SystemAId == system.Id ? link.SystemBId : link.SystemAId)
                .Where(internalIds.Contains)
                .Distinct()
                .ToList();
            var anchor = neighborIds.Count == 0
                ? new Point2(0.5, 0.5)
                : new Point2(
                    neighborIds.Average(id => positions[id].X),
                    neighborIds.Average(id => positions[id].Y));
            var direction = Subtract(anchor, clusterCenter);
            if (Magnitude(direction) < 0.001)
            {
                var angle = (Math.PI * 2.0 * externalPlacements.Count) / Math.Max(1, externalSystems.Count);
                direction = new Point2(Math.Cos(angle), Math.Sin(angle));
            }

            externalPlacements.Add((system.Id, anchor, Scale(direction, 1.0 / Math.Max(0.001, Magnitude(direction)))));
        }

        var placedBoundaryPoints = new List<Point2>();
        var orderedPlacements = externalPlacements
            .OrderBy(item => Math.Atan2(item.Direction.Y, item.Direction.X))
            .ToList();

        for (var index = 0; index < orderedPlacements.Count; index++)
        {
            var placement = orderedPlacements[index];
            var candidate = sectorBoundary.Count >= 3
                ? ProjectRayToBoundary(clusterCenter, placement.Direction, sectorBoundary)
                : Add(placement.Anchor, Scale(placement.Direction, 0.07));

            if (sectorBoundary.Count >= 3)
            {
                var angularOffsets = new[] { 0.0, -0.16, 0.16, -0.32, 0.32, -0.48, 0.48 };
                foreach (var angularOffset in angularOffsets)
                {
                    var direction = Rotate(placement.Direction, angularOffset);
                    var projected = ProjectRayToBoundary(clusterCenter, direction, sectorBoundary);
                    if (placedBoundaryPoints.All(existing => Magnitude(Subtract(projected, existing)) >= 0.055))
                    {
                        candidate = projected;
                        break;
                    }
                }
            }

            positions[placement.SystemId] = new Point2(Clamp(candidate.X, 0.12, 0.88), Clamp(candidate.Y, 0.12, 0.88));
            placedBoundaryPoints.Add(positions[placement.SystemId]);
        }
    }

    private static Point2 ProjectRayToBoundary(Point2 origin, Point2 direction, IReadOnlyList<Point2> boundary)
    {
        if (boundary.Count < 2)
        {
            return origin;
        }

        var bestDistance = double.MaxValue;
        var bestPoint = origin;
        for (var index = 0; index < boundary.Count; index++)
        {
            var start = boundary[index];
            var end = boundary[(index + 1) % boundary.Count];
            if (!TryIntersectRayWithSegment(origin, direction, start, end, out var intersection))
            {
                continue;
            }

            var distance = Magnitude(Subtract(intersection, origin));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestPoint = intersection;
            }
        }

        return bestDistance < double.MaxValue ? bestPoint : Add(origin, Scale(direction, 0.07));
    }

    private static bool TryIntersectRayWithSegment(Point2 rayOrigin, Point2 rayDirection, Point2 segmentStart, Point2 segmentEnd, out Point2 intersection)
    {
        var segment = Subtract(segmentEnd, segmentStart);
        var determinant = Cross2(rayDirection, segment);
        if (Math.Abs(determinant) < 0.000001)
        {
            intersection = rayOrigin;
            return false;
        }

        var delta = Subtract(segmentStart, rayOrigin);
        var rayFactor = Cross2(delta, segment) / determinant;
        var segmentFactor = Cross2(delta, rayDirection) / determinant;
        if (rayFactor < 0.0 || segmentFactor < 0.0 || segmentFactor > 1.0)
        {
            intersection = rayOrigin;
            return false;
        }

        intersection = Add(rayOrigin, Scale(rayDirection, rayFactor));
        return true;
    }

    private static Point2 Rotate(Point2 point, double angle)
    {
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        return new Point2((point.X * cos) - (point.Y * sin), (point.X * sin) + (point.Y * cos));
    }

    private static double Cross2(Point2 left, Point2 right) => (left.X * right.Y) - (left.Y * right.X);

    private static string ChooseExitSide(Point2 anchor)
    {
        var dx = anchor.X - 0.5;
        var dy = anchor.Y - 0.5;
        if (Math.Abs(dx) > Math.Abs(dy))
        {
            return dx < 0.0 ? "left" : "right";
        }

        return dy < 0.0 ? "top" : "bottom";
    }

    private static List<double> SpreadAnchors(IReadOnlyList<double> anchors, double min, double max, double spacing)
    {
        var spread = anchors.Select(value => Clamp(value, min, max)).ToList();
        for (var index = 1; index < spread.Count; index++)
        {
            spread[index] = Math.Max(spread[index], spread[index - 1] + spacing);
        }

        for (var index = spread.Count - 2; index >= 0; index--)
        {
            spread[index] = Math.Min(spread[index], spread[index + 1] - spacing);
        }

        for (var index = 0; index < spread.Count; index++)
        {
            spread[index] = Clamp(spread[index], min, max);
        }

        return spread;
    }

    private static double GetNavSpringLength(string gateType)
    {
        return gateType.Equals("Heavy", StringComparison.OrdinalIgnoreCase)
            ? 0.16
            : gateType.Equals("Medium", StringComparison.OrdinalIgnoreCase)
                ? 0.13
                : 0.09;
    }

    private static List<Point2> ExpandHull(IReadOnlyList<Point2> points, double amount)
    {
        var center = new Point2(points.Average(point => point.X), points.Average(point => point.Y));
        return points
            .Select(point =>
            {
                var expanded = Add(center, Scale(Subtract(point, center), 1.0 + amount));
                return new Point2(Clamp(expanded.X, 0.08, 0.92), Clamp(expanded.Y, 0.08, 0.92));
            })
            .ToList();
    }

    private static Point2 Add(Point2 left, Point2 right) => new(left.X + right.X, left.Y + right.Y);

    private static Point2 Subtract(Point2 left, Point2 right) => new(left.X - right.X, left.Y - right.Y);

    private static Point2 Scale(Point2 point, double scalar) => new(point.X * scalar, point.Y * scalar);

    private static Point2 Lerp(Point2 start, Point2 end, double amount) =>
        new(start.X + ((end.X - start.X) * amount), start.Y + ((end.Y - start.Y) * amount));

    private static double Magnitude(Point2 point) => Math.Sqrt((point.X * point.X) + (point.Y * point.Y));

    private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));

    private static List<Point2> BuildTopDownHull(IReadOnlyList<Point2> points)
    {
        var distinct = points
            .GroupBy(point => $"{point.X:0.0000}|{point.Y:0.0000}")
            .Select(group => group.First())
            .OrderBy(point => point.X)
            .ThenBy(point => point.Y)
            .ToList();
        if (distinct.Count <= 2)
        {
            return distinct;
        }

        var lower = new List<Point2>();
        foreach (var point in distinct)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], point) <= 0.0)
            {
                lower.RemoveAt(lower.Count - 1);
            }

            lower.Add(point);
        }

        var upper = new List<Point2>();
        for (var index = distinct.Count - 1; index >= 0; index--)
        {
            var point = distinct[index];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], point) <= 0.0)
            {
                upper.RemoveAt(upper.Count - 1);
            }

            upper.Add(point);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static double Cross(Point2 a, Point2 b, Point2 c)
    {
        return ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
    }

    private static string BuildNavPath(IReadOnlyList<Point2> points)
    {
        if (points.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append($"M {points[0].X:0.00} {points[0].Y:0.00}");
        for (var index = 1; index < points.Count; index++)
        {
            builder.Append($" L {points[index].X:0.00} {points[index].Y:0.00}");
        }

        builder.Append(" Z");
        return builder.ToString();
    }

    private static string Escape(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}

static class TerritoryRegionLinkReportRenderer
{
    public static string Render(GeneratorConfig config, TerritoryRegionStructureData territory)
    {
        var degrees = territory.Regions.ToDictionary(region => region.Index, _ => 0);
        var adjacentDegrees = territory.Regions.ToDictionary(region => region.Index, _ => 0);

        foreach (var link in territory.HeavyGateLinks)
        {
            degrees[link.RegionA]++;
            degrees[link.RegionB]++;
            if (link.IsAdjacent)
            {
                adjacentDegrees[link.RegionA]++;
                adjacentDegrees[link.RegionB]++;
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"UTF-8\" />");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />");
        builder.AppendLine($"  <title>{Escape(config.TerritoryName)} Region Links</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    body { margin: 0; padding: 28px; background: #07111f; color: #e6eef8; font-family: Consolas, 'Courier New', monospace; }");
        builder.AppendLine("    h1 { margin: 0 0 8px; font-size: 30px; font-weight: 500; }");
        builder.AppendLine("    .meta { color: #8fa6be; margin-bottom: 18px; }");
        builder.AppendLine("    .grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; margin-bottom: 24px; }");
        builder.AppendLine("    .card { background: rgba(12, 24, 38, 0.92); border: 1px solid #20364d; border-radius: 14px; padding: 14px 16px; }");
        builder.AppendLine("    .label { color: #8fa6be; font-size: 12px; text-transform: uppercase; letter-spacing: 0.08em; }");
        builder.AppendLine("    .value { font-size: 22px; margin-top: 6px; }");
        builder.AppendLine("    table { width: 100%; border-collapse: collapse; margin-top: 12px; }");
        builder.AppendLine("    th, td { border-bottom: 1px solid #20364d; padding: 10px 12px; text-align: left; vertical-align: top; }");
        builder.AppendLine("    th { color: #9db4ca; font-size: 12px; text-transform: uppercase; letter-spacing: 0.08em; }");
        builder.AppendLine("    tr:hover td { background: rgba(18, 34, 52, 0.72); }");
        builder.AppendLine("    .adjacent { color: #f4f1de; }");
        builder.AppendLine("    .range { color: #f6bd60; }");
        builder.AppendLine("    .section { margin-top: 26px; }");
        builder.AppendLine("    .mono { white-space: nowrap; }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine($"  <h1>{Escape(config.TerritoryName)} Region Link Report</h1>");
        builder.AppendLine($"  <div class=\"meta\">Geography {Escape(config.GeographySeed)} | Heavy pair span {config.MinimumStarDistanceLy * 9.0:0.0}-{config.MinimumStarDistanceLy * 12.0:0.0} ly | {territory.HeavyGateLinks.Count} heavy links</div>");
        builder.AppendLine("  <div class=\"grid\">");
        builder.AppendLine($"    <div class=\"card\"><div class=\"label\">Regions</div><div class=\"value\">{territory.Regions.Count}</div></div>");
        builder.AppendLine($"    <div class=\"card\"><div class=\"label\">Adjacent Links</div><div class=\"value\">{territory.HeavyGateLinks.Count(link => link.IsAdjacent)}</div></div>");
        builder.AppendLine($"    <div class=\"card\"><div class=\"label\">Range Links</div><div class=\"value\">{territory.HeavyGateLinks.Count(link => !link.IsAdjacent)}</div></div>");
        builder.AppendLine("  </div>");
        builder.AppendLine("  <div class=\"section\">");
        builder.AppendLine("    <h2>Per Region Summary</h2>");
        builder.AppendLine("    <table>");
        builder.AppendLine("      <thead><tr><th>Region</th><th>Total Links</th><th>Adjacent Links</th><th>Connected Regions</th></tr></thead>");
        builder.AppendLine("      <tbody>");

        foreach (var region in territory.Regions.OrderBy(region => region.Index))
        {
            var neighbors = territory.HeavyGateLinks
                .Where(link => link.RegionA == region.Index || link.RegionB == region.Index)
                .Select(link => link.RegionA == region.Index ? link.RegionB : link.RegionA)
                .Distinct()
                .OrderBy(index => index)
                .Select(index => territory.Regions[index].Name);

            builder.AppendLine($"        <tr><td>{Escape(region.Name)}</td><td>{degrees[region.Index]}</td><td>{adjacentDegrees[region.Index]}</td><td>{Escape(string.Join(", ", neighbors))}</td></tr>");
        }

        builder.AppendLine("      </tbody>");
        builder.AppendLine("    </table>");
        builder.AppendLine("  </div>");
        builder.AppendLine("  <div class=\"section\">");
        builder.AppendLine("    <h2>Heavy Link Detail</h2>");
        builder.AppendLine("    <table>");
        builder.AppendLine("      <thead><tr><th>#</th><th>Type</th><th>Regions</th><th>Distance</th><th>System A</th><th>System B</th></tr></thead>");
        builder.AppendLine("      <tbody>");

        for (var index = 0; index < territory.HeavyGateLinks.Count; index++)
        {
            var link = territory.HeavyGateLinks[index];
            var cssClass = link.IsAdjacent ? "adjacent" : "range";
            var typeLabel = link.IsAdjacent ? "Adjacent" : "Range";
            builder.AppendLine($"        <tr><td>{index + 1}</td><td class=\"{cssClass}\">{typeLabel}</td><td>{territory.Regions[link.RegionA].Name} - {territory.Regions[link.RegionB].Name}</td><td class=\"mono\">{link.DistanceLy:0.00} ly</td><td class=\"mono\">({link.SystemA.X:0.00}, {link.SystemA.Y:0.00}, {link.SystemA.Z:0.00})</td><td class=\"mono\">({link.SystemB.X:0.00}, {link.SystemB.Y:0.00}, {link.SystemB.Z:0.00})</td></tr>");
        }

        builder.AppendLine("      </tbody>");
        builder.AppendLine("    </table>");
        builder.AppendLine("  </div>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}

static class SectorNavigation3DHtmlRenderer
{
    public static string Render(GeneratorConfig config, RegionSectorSet regionSectors, SectorCell sector, TerritoryNetworkSnapshot networkSnapshot)
    {
        var systemById = networkSnapshot.Systems.ToDictionary(system => system.Id);
        var internalSystems = networkSnapshot.Systems
            .Where(system => system.RegionIndex == regionSectors.Region.Index && system.SectorIndex == sector.Index)
            .OrderBy(system => system.Address)
            .ToList();
        var internalIds = internalSystems.Select(system => system.Id).ToHashSet();
        var relevantLinks = networkSnapshot.Links
            .Where(link => internalIds.Contains(link.SystemAId) || internalIds.Contains(link.SystemBId))
            .ToList();
        var relevantSystemIds = relevantLinks
            .SelectMany(link => new[] { link.SystemAId, link.SystemBId })
            .Concat(internalIds)
            .Distinct()
            .ToHashSet();
        var relevantSystems = relevantSystemIds
            .Select(id => systemById[id])
            .OrderBy(system => internalIds.Contains(system.Id) ? 0 : 1)
            .ThenBy(system => system.Address)
            .ToList();
        var focusSystems = internalSystems.Count > 0 ? internalSystems : relevantSystems;
        var center = BuildNav3DCenter(focusSystems);
        var span = BuildNav3DSpan(focusSystems, center);
        var systemsJson = BuildNav3DSystemsJson(relevantSystems, internalIds, center, sector.ColorHex);
        var linksJson = BuildNav3DLinksJson(relevantLinks, systemById, internalIds, center);

        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"UTF-8\" />");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />");
        builder.AppendLine($"  <title>{Escape(config.TerritoryName)} {Escape(sector.Name)} NAV3D</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    :root { color-scheme: dark; }");
        builder.AppendLine("    body { margin: 0; background: radial-gradient(circle at top, #0b1626, #04070d 65%); color: #e7eef7; font-family: Consolas, 'Courier New', monospace; }");
        builder.AppendLine("    .shell { padding: 24px 28px 18px; }");
        builder.AppendLine("    h1 { margin: 0 0 8px; font-size: 28px; font-weight: 500; }");
        builder.AppendLine("    .meta { color: #8fa6be; font-size: 14px; margin-bottom: 14px; }");
        builder.AppendLine("    .hint { color: #9db4ca; font-size: 13px; margin-bottom: 14px; }");
        builder.AppendLine("    canvas { width: min(1200px, calc(100vw - 56px)); height: min(840px, calc(100vh - 160px)); border: 1px solid #22384d; border-radius: 18px; background: linear-gradient(180deg, rgba(10,20,34,0.92), rgba(3,7,12,0.96)); display: block; box-shadow: 0 18px 48px rgba(0,0,0,0.32); cursor: grab; }");
        builder.AppendLine("    canvas.dragging { cursor: grabbing; }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("  <div class=\"shell\" id=\"viewer-shell\">");
        builder.AppendLine($"    <h1>{Escape(sector.Name)} NAV3D</h1>");
        builder.AppendLine($"    <div class=\"meta\">{Escape(config.TerritoryName)} | Spatially accurate sector navigation map | In-sector systems {internalSystems.Count} | Exit systems {relevantSystems.Count - internalSystems.Count} | Links {relevantLinks.Count}</div>");
        builder.AppendLine("    <div class=\"hint\">Left-drag to rotate. Right-drag or Shift-drag to pan. W/S dolly in and out. Scroll to zoom. Double-click to reset the view.</div>");
        builder.AppendLine("    <canvas id=\"view\" width=\"1200\" height=\"840\"></canvas>");
        builder.AppendLine("  </div>");
        builder.AppendLine("  <script>");
        builder.AppendLine($"    const systems = {systemsJson};");
        builder.AppendLine($"    const links = {linksJson};");
        builder.AppendLine($"    const maxSpan = {span:0.###};");
        builder.AppendLine("    const shell = document.getElementById('viewer-shell');");
        builder.AppendLine("    const canvas = document.getElementById('view');");
        builder.AppendLine("    const ctx = canvas.getContext('2d');");
        builder.AppendLine("    let yaw = -0.72;");
        builder.AppendLine("    let pitch = 0.48;");
        builder.AppendLine("    let zoom = 4.4;");
        builder.AppendLine("    let panX = 0;");
        builder.AppendLine("    let panY = 0;");
        builder.AppendLine("    let dolly = 0;");
        builder.AppendLine("    let dragging = false;");
        builder.AppendLine("    let dragMode = 'rotate';");
        builder.AppendLine("    let zoomTargetActive = false;");
        builder.AppendLine("    let lastX = 0;");
        builder.AppendLine("    let lastY = 0;");
        builder.AppendLine("    const baseScale = Math.max(3.4, 520 / Math.max(1, maxSpan));");
        builder.AppendLine("    const distanceBase = Math.max(54, maxSpan * 0.8);");
        builder.AppendLine("    function resetView() { yaw = -0.72; pitch = 0.48; zoom = 4.4; panX = 0; panY = 0; dolly = 0; render(); }");
        builder.AppendLine("    function applyZoom(deltaY) { const factor = Math.exp(deltaY * 0.0030); zoom = Math.max(0.01, Math.min(56.0, zoom * factor)); render(); }");
        builder.AppendLine("    function rotate(point) {");
        builder.AppendLine("      const cy = Math.cos(yaw), sy = Math.sin(yaw);");
        builder.AppendLine("      const cp = Math.cos(pitch), sp = Math.sin(pitch);");
        builder.AppendLine("      const x1 = (point.x * cy) - (point.z * sy);");
        builder.AppendLine("      const z1 = (point.x * sy) + (point.z * cy);");
        builder.AppendLine("      const y2 = (point.y * cp) - (z1 * sp);");
        builder.AppendLine("      const z2 = (point.y * sp) + (z1 * cp);");
        builder.AppendLine("      return { x: x1, y: y2, z: z2 };");
        builder.AppendLine("    }");
        builder.AppendLine("    function project(point) {");
        builder.AppendLine("      const distance = distanceBase + (65 * zoom);");
        builder.AppendLine("      const shiftedZ = Math.min(point.z + dolly, distance - 2.0);");
        builder.AppendLine("      const perspective = distance / (distance - shiftedZ);");
        builder.AppendLine("      return { x: canvas.width / 2 + panX + (point.x * perspective * baseScale), y: canvas.height / 2 + panY + (point.y * perspective * baseScale), z: shiftedZ, scale: perspective };");
        builder.AppendLine("    }");
        builder.AppendLine("    function render() {");
        builder.AppendLine("      ctx.clearRect(0, 0, canvas.width, canvas.height);");
        builder.AppendLine("      ctx.fillStyle = '#07101b';");
        builder.AppendLine("      ctx.fillRect(0, 0, canvas.width, canvas.height);");
        builder.AppendLine("      const projectedLinks = links.map(link => {");
        builder.AppendLine("        const rotatedStart = rotate(link.start);");
        builder.AppendLine("        const rotatedEnd = rotate(link.end);");
        builder.AppendLine("        return { start: project(rotatedStart), end: project(rotatedEnd), gateType: link.gateType, isExternal: link.isExternal, depth: (rotatedStart.z + rotatedEnd.z) * 0.5 };");
        builder.AppendLine("      }).sort((left, right) => left.depth - right.depth);");
        builder.AppendLine("      for (const link of projectedLinks) {");
        builder.AppendLine("        ctx.strokeStyle = gateStroke(link.gateType, link.isExternal);");
        builder.AppendLine("        ctx.lineWidth = gateWidth(link.gateType);");
        builder.AppendLine("        if (link.isExternal) { ctx.setLineDash([7, 5]); } else { ctx.setLineDash([]); }");
        builder.AppendLine("        ctx.beginPath();");
        builder.AppendLine("        ctx.moveTo(link.start.x, link.start.y);");
        builder.AppendLine("        ctx.lineTo(link.end.x, link.end.y);");
        builder.AppendLine("        ctx.stroke();");
        builder.AppendLine("      }");
        builder.AppendLine("      ctx.setLineDash([]);");
        builder.AppendLine("      const projectedSystems = systems.map(item => { const rotated = rotate(item.position); return { item, projected: project(rotated), depth: rotated.z }; }).sort((left, right) => left.depth - right.depth);");
        builder.AppendLine("      for (const entry of projectedSystems) {");
        builder.AppendLine("        drawSystem(entry.projected, entry.item);");
        builder.AppendLine("      }");
        builder.AppendLine("    }");
        builder.AppendLine("    function gateStroke(gateType, isExternal) {");
        builder.AppendLine("      if (gateType === 'Heavy') return isExternal ? 'rgba(217, 164, 65, 0.72)' : 'rgba(217, 164, 65, 0.88)';");
        builder.AppendLine("      if (gateType === 'Medium') return isExternal ? 'rgba(120, 189, 221, 0.72)' : 'rgba(120, 189, 221, 0.86)';");
        builder.AppendLine("      return isExternal ? 'rgba(183, 240, 199, 0.52)' : 'rgba(183, 240, 199, 0.64)';");
        builder.AppendLine("    }");
        builder.AppendLine("    function gateWidth(gateType) {");
        builder.AppendLine("      return gateType === 'Heavy' ? 2.5 : gateType === 'Medium' ? 2.1 : 1.3;");
        builder.AppendLine("    }");
        builder.AppendLine("    function drawSystem(point, item) {");
        builder.AppendLine("      const radius = Math.max(item.radius, point.scale * item.radius);");
        builder.AppendLine("      ctx.fillStyle = item.glow;");
        builder.AppendLine("      ctx.beginPath();");
        builder.AppendLine("      ctx.arc(point.x, point.y, radius * 2.0, 0, Math.PI * 2);");
        builder.AppendLine("      ctx.fill();");
        builder.AppendLine("      ctx.fillStyle = item.fill;");
        builder.AppendLine("      ctx.strokeStyle = item.stroke;");
        builder.AppendLine("      ctx.lineWidth = item.strokeWidth;");
        builder.AppendLine("      ctx.beginPath();");
        builder.AppendLine("      ctx.arc(point.x, point.y, radius, 0, Math.PI * 2);");
        builder.AppendLine("      ctx.fill();");
        builder.AppendLine("      ctx.stroke();");
        builder.AppendLine("      ctx.fillStyle = item.text;");
        builder.AppendLine("      ctx.font = item.isExternal ? '10px Consolas' : '11px Consolas';");
        builder.AppendLine("      ctx.fillText(item.label, point.x + 7, point.y - 7);");
        builder.AppendLine("    }");
        builder.AppendLine("    canvas.addEventListener('contextmenu', event => event.preventDefault());");
        builder.AppendLine("    canvas.addEventListener('pointerdown', event => { dragging = true; dragMode = (event.button === 2 || event.shiftKey) ? 'pan' : 'rotate'; lastX = event.clientX; lastY = event.clientY; canvas.classList.add('dragging'); });");
        builder.AppendLine("    shell.addEventListener('pointerenter', () => { zoomTargetActive = true; });");
        builder.AppendLine("    shell.addEventListener('pointerleave', () => { zoomTargetActive = false; });");
        builder.AppendLine("    window.addEventListener('pointerup', () => { dragging = false; canvas.classList.remove('dragging'); });");
        builder.AppendLine("    window.addEventListener('pointermove', event => {");
        builder.AppendLine("      if (!dragging) return;");
        builder.AppendLine("      const dx = event.clientX - lastX;");
        builder.AppendLine("      const dy = event.clientY - lastY;");
        builder.AppendLine("      lastX = event.clientX;");
        builder.AppendLine("      lastY = event.clientY;");
        builder.AppendLine("      if (dragMode === 'pan') { panX += dx; panY += dy; } else { yaw += dx * 0.008; pitch = Math.max(-1.35, Math.min(1.35, pitch + dy * 0.008)); }");
        builder.AppendLine("      render();");
        builder.AppendLine("    });");
        builder.AppendLine("    shell.addEventListener('wheel', event => { event.preventDefault(); applyZoom(event.deltaY); }, { passive: false });");
        builder.AppendLine("    window.addEventListener('wheel', event => { if (!zoomTargetActive) return; event.preventDefault(); applyZoom(event.deltaY); }, { passive: false });");
        builder.AppendLine("    window.addEventListener('keydown', event => { if (!zoomTargetActive) return; const moveStep = Math.max(1.2, maxSpan * 0.08); if (event.key === 'w' || event.key === 'W') { dolly += moveStep; } else if (event.key === 's' || event.key === 'S') { dolly -= moveStep; } else { return; } event.preventDefault(); render(); });");
        builder.AppendLine("    canvas.addEventListener('dblclick', () => resetView());");
        builder.AppendLine("    render();");
        builder.AppendLine("  </script>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");

        return builder.ToString();
    }

    private static Point3 BuildNav3DCenter(IReadOnlyList<NetworkSystemNode> systems)
    {
        if (systems.Count == 0)
        {
            return new Point3(0, 0, 0);
        }

        return new Point3(
            systems.Average(system => system.Position.X),
            systems.Average(system => system.Position.Y),
            systems.Average(system => system.Position.Z));
    }

    private static double BuildNav3DSpan(IReadOnlyList<NetworkSystemNode> systems, Point3 center)
    {
        if (systems.Count == 0)
        {
            return 1.0;
        }

        var maxExtent = 1.0;
        foreach (var system in systems)
        {
            maxExtent = Math.Max(maxExtent, Math.Abs(system.Position.X - center.X));
            maxExtent = Math.Max(maxExtent, Math.Abs(system.Position.Y - center.Y));
            maxExtent = Math.Max(maxExtent, Math.Abs(system.Position.Z - center.Z));
        }

        return maxExtent * 2.0;
    }

    private static string BuildNav3DSystemsJson(IReadOnlyList<NetworkSystemNode> systems, ISet<int> internalIds, Point3 center, string internalSectorColor)
    {
        var builder = new StringBuilder();
        builder.Append('[');
        for (var index = 0; index < systems.Count; index++)
        {
            var system = systems[index];
            if (index > 0)
            {
                builder.Append(',');
            }

            var isInternal = internalIds.Contains(system.Id);
            var hasHeavy = system.Gates.Any(gate => gate.GateType == "Heavy");
            var hasMedium = system.Gates.Any(gate => gate.GateType == "Medium");
            var radius = hasHeavy ? 3.8 : hasMedium ? 3.1 : isInternal ? 2.4 : 2.1;
            var fill = isInternal ? "rgba(31,40,49,0.98)" : "rgba(127,142,163,0.92)";
            var stroke = hasHeavy ? "rgba(217,164,65,0.94)" : hasMedium ? "rgba(120,189,221,0.92)" : (isInternal ? "rgba(174,185,197,0.92)" : "rgba(243,247,251,0.86)");
            var glow = hasHeavy ? "rgba(217,164,65,0.14)" : hasMedium ? "rgba(120,189,221,0.14)" : (isInternal ? "rgba(96,112,128,0.12)" : "rgba(143,163,191,0.12)");
            var text = isInternal ? "rgba(245,247,250,0.96)" : "rgba(243,247,251,0.92)";
            var label = isInternal ? system.Name : system.Address;
            var position = new Point3(system.Position.X - center.X, system.Position.Y - center.Y, system.Position.Z - center.Z);
            builder.Append($"{{\"position\":{{\"x\":{position.X:0.###},\"y\":{position.Y:0.###},\"z\":{position.Z:0.###}}},\"fill\":\"{fill}\",\"stroke\":\"{stroke}\",\"glow\":\"{glow}\",\"text\":\"{text}\",\"radius\":{radius:0.0},\"strokeWidth\":{(hasHeavy ? 1.4 : hasMedium ? 1.2 : 1.0):0.0},\"label\":\"{Escape(label)}\",\"isExternal\":{(!isInternal).ToString().ToLowerInvariant()}}}");
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string BuildNav3DLinksJson(IReadOnlyList<NetworkLinkEdge> links, IReadOnlyDictionary<int, NetworkSystemNode> systemById, ISet<int> internalIds, Point3 center)
    {
        var builder = new StringBuilder();
        builder.Append('[');
        for (var index = 0; index < links.Count; index++)
        {
            var link = links[index];
            if (index > 0)
            {
                builder.Append(',');
            }

            var start = systemById[link.SystemAId].Position;
            var end = systemById[link.SystemBId].Position;
            var isExternal = !(internalIds.Contains(link.SystemAId) && internalIds.Contains(link.SystemBId));
            builder.Append($"{{\"start\":{{\"x\":{start.X - center.X:0.###},\"y\":{start.Y - center.Y:0.###},\"z\":{start.Z - center.Z:0.###}}},\"end\":{{\"x\":{end.X - center.X:0.###},\"y\":{end.Y - center.Y:0.###},\"z\":{end.Z - center.Z:0.###}}},\"gateType\":\"{link.GateType}\",\"isExternal\":{isExternal.ToString().ToLowerInvariant()}}}");
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string Escape(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}

static class TerritorySolarSystemValidationReportRenderer
{
    public static TerritoryNetworkSnapshot BuildSnapshot(GeneratorConfig config, TerritoryRegionStructureData territory)
    {
        var regionSectorMap = territory.RegionSectors.ToDictionary(item => item.Region.Index);
        var drafts = new Dictionary<int, SolarSystemDraft>();
        var sectorSystemIds = territory.RegionSectors
            .SelectMany(regionSectors => regionSectors.Sectors.Select(sector => ((regionSectors.Region.Index, sector.Index), new List<int>())))
            .ToDictionary(item => item.Item1, item => item.Item2);
        var nextId = 1;

        void AddDraft(SolarSystemDraft draft)
        {
            drafts.Add(draft.Id, draft);
            sectorSystemIds[(draft.RegionIndex, draft.SectorIndex)].Add(draft.Id);
        }

        foreach (var regionSectors in territory.RegionSectors.OrderBy(item => item.Region.Index))
        {
            for (var linkIndex = 0; linkIndex < regionSectors.GateLinks.Count; linkIndex++)
            {
                var link = regionSectors.GateLinks[linkIndex];
                var leftSystem = new SolarSystemDraft(
                    nextId++,
                    regionSectors.Region.Index,
                    link.SectorA,
                    link.SystemA,
                    "Medium Gate Anchor",
                    $"Medium gate {regionSectors.Region.Name}:{link.SectorA:D2}-{link.SectorB:D2} #{linkIndex + 1}",
                    new List<SolarSystemGateDraft>(),
                    ValidateSystem(territory, regionSectors.Region.Index, link.SectorA, link.SystemA));

                var rightSystem = new SolarSystemDraft(
                    nextId++,
                    regionSectors.Region.Index,
                    link.SectorB,
                    link.SystemB,
                    "Medium Gate Anchor",
                    $"Medium gate {regionSectors.Region.Name}:{link.SectorA:D2}-{link.SectorB:D2} #{linkIndex + 1}",
                    new List<SolarSystemGateDraft>(),
                    ValidateSystem(territory, regionSectors.Region.Index, link.SectorB, link.SystemB));

                leftSystem.Gates.Add(new SolarSystemGateDraft("Medium", link.DistanceLy, rightSystem.Id, $"{regionSectors.Region.Name}-S{link.SectorB:D2}"));
                rightSystem.Gates.Add(new SolarSystemGateDraft("Medium", link.DistanceLy, leftSystem.Id, $"{regionSectors.Region.Name}-S{link.SectorA:D2}"));
                AddDraft(leftSystem);
                AddDraft(rightSystem);
            }
        }

        for (var linkIndex = 0; linkIndex < territory.HeavyGateLinks.Count; linkIndex++)
        {
            var link = territory.HeavyGateLinks[linkIndex];
            var leftSector = LocateSectorIndex(regionSectorMap[link.RegionA], link.SystemA);
            var rightSector = LocateSectorIndex(regionSectorMap[link.RegionB], link.SystemB);

            var leftSystem = new SolarSystemDraft(
                nextId++,
                link.RegionA,
                leftSector,
                link.SystemA,
                "Heavy Gate Anchor",
                $"Heavy gate {territory.Regions[link.RegionA].Name}-{territory.Regions[link.RegionB].Name} #{linkIndex + 1}",
                new List<SolarSystemGateDraft>(),
                ValidateSystem(territory, link.RegionA, leftSector, link.SystemA));

            var rightSystem = new SolarSystemDraft(
                nextId++,
                link.RegionB,
                rightSector,
                link.SystemB,
                "Heavy Gate Anchor",
                $"Heavy gate {territory.Regions[link.RegionA].Name}-{territory.Regions[link.RegionB].Name} #{linkIndex + 1}",
                new List<SolarSystemGateDraft>(),
                ValidateSystem(territory, link.RegionB, rightSector, link.SystemB));

            leftSystem.Gates.Add(new SolarSystemGateDraft("Heavy", link.DistanceLy, rightSystem.Id, territory.Regions[link.RegionB].Name));
            rightSystem.Gates.Add(new SolarSystemGateDraft("Heavy", link.DistanceLy, leftSystem.Id, territory.Regions[link.RegionA].Name));
            AddDraft(leftSystem);
            AddDraft(rightSystem);
        }

        var randomSystemsBySector = AllocateRandomSystemsToSectors(config.StarCount, territory.RegionSectors);
        foreach (var regionSectors in territory.RegionSectors.OrderBy(item => item.Region.Index))
        {
            foreach (var sector in regionSectors.Sectors.OrderBy(item => item.Index))
            {
                var sectorKey = (regionSectors.Region.Index, sector.Index);
                var existingSystems = sectorSystemIds[sectorKey]
                    .Select(id => drafts[id])
                    .Select(draft => new ExistingSystemReference(draft.Id, draft.Position, draft.SourceType, draft.SourceDescription))
                    .ToList();
                var ownedPoints = regionSectors.OwnedSamples
                    .Where(sample => sample.OwnerIndex == sector.Index)
                    .Select(sample => sample.Position)
                    .ToList();

                var sectorPopulation = BuildSectorPopulation(
                    config,
                    territory,
                    regionSectors,
                    sector,
                    ownedPoints,
                    existingSystems,
                    randomSystemsBySector.GetValueOrDefault(sectorKey),
                    () => nextId++);

                foreach (var draft in sectorPopulation.NewSystems)
                {
                    AddDraft(draft);
                }

                foreach (var lightGate in sectorPopulation.LightGates)
                {
                    drafts[lightGate.SystemAId].Gates.Add(new SolarSystemGateDraft("Light", lightGate.DistanceLy, lightGate.SystemBId, lightGate.Scope));
                    drafts[lightGate.SystemBId].Gates.Add(new SolarSystemGateDraft("Light", lightGate.DistanceLy, lightGate.SystemAId, lightGate.Scope));
                }
            }
        }

        var orderedDrafts = drafts.Values
            .OrderBy(system => system.RegionIndex)
            .ThenBy(system => system.SectorIndex)
            .ThenBy(system => system.Position.X)
            .ThenBy(system => system.Position.Y)
            .ThenBy(system => system.Position.Z)
            .ToList();

        var profileById = BuildSystemProfiles(config, orderedDrafts);

        var sectorCounters = new Dictionary<(int RegionIndex, int SectorIndex), int>();
        var addressById = new Dictionary<int, string>();
        foreach (var draft in orderedDrafts)
        {
            var key = (draft.RegionIndex, draft.SectorIndex);
            var ordinal = sectorCounters.TryGetValue(key, out var current) ? current + 1 : 1;
            sectorCounters[key] = ordinal;
            var systemName = $"SYS{ordinal:D2}";
            var address = $"T0R{draft.RegionIndex:D2}S{draft.SectorIndex:D2}-{systemName}";
            addressById[draft.Id] = address;
        }

        var systems = orderedDrafts
            .Select(draft => new NetworkSystemNode(
                draft.Id,
                draft.RegionIndex,
                draft.SectorIndex,
                draft.Position,
                profileById[draft.Id].DisplayName,
                addressById[draft.Id],
                draft.SourceType,
                draft.SourceDescription,
                draft.Gates
                    .Select(gate => new NetworkGate(gate.TargetSystemId, gate.GateType, gate.DistanceLy, gate.TargetScope))
                    .OrderBy(gate => gate.GateType)
                    .ThenBy(gate => gate.TargetSystemId)
                    .ToList()))
            .ToList();

        var links = orderedDrafts
            .SelectMany(draft => draft.Gates.Select(gate => new { draft.Id, Gate = gate }))
            .Where(item => item.Id < item.Gate.TargetSystemId)
            .Select(item => new NetworkLinkEdge(item.Id, item.Gate.TargetSystemId, item.Gate.GateType, item.Gate.DistanceLy, item.Gate.TargetScope))
            .ToList();

        return new TerritoryNetworkSnapshot(systems, links);
    }

    public static SolarSystemReport Build(GeneratorConfig config, TerritoryRegionStructureData territory)
    {
        var systems = BuildSystems(config, territory, out var routeAudit);
        var html = Render(config, territory, systems, routeAudit);
        var pathReportFileName = $"DIAG_PATHS_FROM_{SanitizeFileComponent(routeAudit.StartAddress)}.HTML";
        var pathReportHtml = RenderPathReport(config, systems, routeAudit);
        return new SolarSystemReport(html, systems.Count, pathReportFileName, pathReportHtml);
    }

    private static Dictionary<int, GeneratedSystemProfile> BuildSystemProfiles(GeneratorConfig config, IReadOnlyList<SolarSystemDraft> drafts)
    {
        var profiles = new Dictionary<int, GeneratedSystemProfile>(drafts.Count);
        for (var index = 0; index < drafts.Count; index++)
        {
            var draft = drafts[index];
            profiles[draft.Id] = BuildSystemProfile(config, draft, index + 1);
        }

        return profiles;
    }

    private static GeneratedSystemProfile BuildSystemProfile(GeneratorConfig config, SolarSystemDraft draft, int creationOrder)
    {
        var seed = StableSeedHasher.HashToInt32($"{config.HistorySeed}:system-profile:{draft.RegionIndex}:{draft.SectorIndex}:{draft.Id}:{(int)Math.Round(draft.Position.X * 1000.0)}:{(int)Math.Round(draft.Position.Y * 1000.0)}:{(int)Math.Round(draft.Position.Z * 1000.0)}");
        var random = new Random(seed);
        var star = GenerateStarProfile(random);
        var planets = GeneratePlanetProfiles(random, star, draft.SourceType);
        var displayName = GenerateSystemDisplayName(draft.RegionIndex, draft.SectorIndex, creationOrder, star, planets);
        star = star with { StarName = displayName + " Primary" };
        planets = ApplyPlanetNames(planets, displayName);
        var habitableCount = planets.Count(planet => planet.IsHabitable);
        var totalMoons = planets.Sum(planet => planet.MoonCount);
        var ringedPlanets = planets.Count(planet => planet.HasRings);
        var giantCount = planets.Count(planet => planet.PlanetType.Contains("Giant", StringComparison.Ordinal));

        var stellarSummary = $"{star.Classification} primary {star.StarName} | {star.MassSolar:0.00} Msol | {star.TemperatureK:0} K | {star.AgeBillionYears:0.0} Gyr | Habitable zone {star.HabitableZoneInnerAu:0.00}-{star.HabitableZoneOuterAu:0.00} AU";
        var contentsSummary = $"System with {draft.Gates.Count} gate(s): {draft.Gates.Count(gate => gate.GateType == "Heavy")} heavy, {draft.Gates.Count(gate => gate.GateType == "Medium")} medium, {draft.Gates.Count(gate => gate.GateType == "Light")} light. Generated profile: {planets.Count} planets, {totalMoons} moons, {ringedPlanets} ringed world(s), {giantCount} giant world(s), {habitableCount} habitable candidate(s).";
        var flavorText = BuildFlavorText(draft.SourceType, star, planets, habitableCount, ringedPlanets);

        return new GeneratedSystemProfile(displayName, stellarSummary, contentsSummary, flavorText, planets);
    }

    private static string BuildFlavorText(string sourceType, GeneratedStarProfile star, IReadOnlyList<GeneratedPlanetProfile> planets, int habitableCount, int ringedPlanets)
    {
        var innerWorld = planets.FirstOrDefault();
        var outerWorld = planets.LastOrDefault();
        var habitabilityText = habitableCount > 0
            ? $"Survey notes suggest {habitableCount} temperate world candidate(s) in the {star.Classification} habitable band"
            : $"Survey notes show no stable habitable worlds around this {star.Classification} primary";
        var ringText = ringedPlanets > 0
            ? $", with {ringedPlanets} ringed planet(s) shaping the outer lanes"
            : ".";

        if (innerWorld is null || outerWorld is null)
        {
            return habitabilityText + ringText;
        }

        return $"{sourceType} profile. Inner orbit opens with {innerWorld.PlanetType.ToLowerInvariant()} conditions at {innerWorld.SemiMajorAxisAu:0.00} AU while the outer fringe ends in {outerWorld.PlanetType.ToLowerInvariant()} territory at {outerWorld.SemiMajorAxisAu:0.00} AU. {habitabilityText}{ringText}";
    }

    private static string GenerateSystemDisplayName(int regionIndex, int sectorIndex, int creationOrder, GeneratedStarProfile star, IReadOnlyList<GeneratedPlanetProfile> planets)
    {
        var trsPrefix = BuildTrsPrefix(regionIndex, sectorIndex);
        var proceduralName = GenerateProceduralSystemName(creationOrder, star, planets);
        return $"{trsPrefix}-{proceduralName}";
    }

    private static string BuildTrsPrefix(int regionIndex, int sectorIndex)
    {
        return $"T0R{regionIndex:D2}S{sectorIndex:D2}";
    }

    private static string GenerateProceduralSystemName(int creationOrder, GeneratedStarProfile star, IReadOnlyList<GeneratedPlanetProfile> planets)
    {
        var systemId = $"SYS_{creationOrder:D8}";
        var starTypeLetter = GetStarTypeLetter(star.Classification);
        var totalMoons = planets.Sum(planet => planet.MoonCount);
        var suffixLetter = GetDeterministicSystemLetter(systemId);
        return $"{starTypeLetter}{creationOrder:D8}-{planets.Count}{suffixLetter}{totalMoons}";
    }

    private static char GetStarTypeLetter(string classification)
    {
        return classification[^1];
    }

    private static char GetDeterministicSystemLetter(string systemId)
    {
        var letterIndex = 0;
        foreach (var character in systemId)
        {
            letterIndex = unchecked(letterIndex + character);
        }

        return (char)('A' + (letterIndex % 26));
    }

    private static GeneratedStarProfile GenerateStarProfile(Random random)
    {
        var roll = random.Next(1000);
        var classification = roll switch
        {
            <= 765 => "Class M",
            <= 887 => "Class K",
            <= 963 => "Class G",
            <= 993 => "Class F",
            <= 999 => "Class A",
            _ => "Class G"
        };

        var (minMass, maxMass, minTemp, maxTemp) = classification switch
        {
            "Class M" => (0.08, 0.45, 2400.0, 3700.0),
            "Class K" => (0.45, 0.80, 3900.0, 5200.0),
            "Class G" => (0.80, 1.05, 5300.0, 6000.0),
            "Class F" => (1.05, 1.40, 6000.0, 7300.0),
            _ => (1.40, 2.10, 7600.0, 9800.0)
        };

        var massSolar = minMass + (random.NextDouble() * (maxMass - minMass));
        var luminositySolar = Math.Max(0.02, Math.Pow(massSolar, massSolar < 0.43 ? 2.3 : massSolar < 2.0 ? 4.0 : 3.5));
        var temperatureK = minTemp + (random.NextDouble() * (maxTemp - minTemp));
        var ageBillionYears = classification == "Class A"
            ? 0.4 + (random.NextDouble() * 1.6)
            : 1.0 + (random.NextDouble() * 8.5);
        var hzInner = Math.Sqrt(luminositySolar) * 0.75;
        var hzOuter = Math.Sqrt(luminositySolar) * 1.77;

        return new GeneratedStarProfile(string.Empty, classification, massSolar, temperatureK, luminositySolar, ageBillionYears, hzInner, hzOuter);
    }

    private static List<GeneratedPlanetProfile> ApplyPlanetNames(IReadOnlyList<GeneratedPlanetProfile> planets, string displayName)
    {
        var renamedPlanets = new List<GeneratedPlanetProfile>(planets.Count);
        for (var index = 0; index < planets.Count; index++)
        {
            renamedPlanets.Add(planets[index] with { Name = displayName + " " + ToRomanNumeral(index + 1) });
        }

        return renamedPlanets;
    }

    private static List<GeneratedPlanetProfile> GeneratePlanetProfiles(Random random, GeneratedStarProfile star, string sourceType)
    {
        var planetCount = sourceType.Contains("Heavy", StringComparison.Ordinal) ? random.Next(7, 13) : sourceType.Contains("Medium", StringComparison.Ordinal) ? random.Next(6, 11) : random.Next(4, 10);
        var orbitalDistances = GenerateOrbitalDistances(random, planetCount, star);
        var planets = new List<GeneratedPlanetProfile>(planetCount);

        for (var index = 0; index < orbitalDistances.Count; index++)
        {
            var semiMajorAxis = orbitalDistances[index];
            var eccentricity = random.NextDouble() < 0.14
                ? 0.12 + (random.NextDouble() * 0.18)
                : 0.01 + (random.NextDouble() * 0.10);
            var inclination = random.NextDouble() < 0.10
                ? 4.0 + (random.NextDouble() * 8.0)
                : 0.2 + (random.NextDouble() * 3.5);
            var planetType = DeterminePlanetType(random, semiMajorAxis, star, eccentricity);
            var hasRings = planetType.Contains("Giant", StringComparison.Ordinal) ? random.NextDouble() < 0.42 : random.NextDouble() < 0.07;
            var moonCount = GenerateMoonCount(random, planetType);
            var orbitalPeriodYears = Math.Sqrt((semiMajorAxis * semiMajorAxis * semiMajorAxis) / Math.Max(0.08, star.MassSolar));
            var isHabitable = IsHabitableCandidate(planetType, semiMajorAxis, star, eccentricity);
            planets.Add(new GeneratedPlanetProfile(string.Empty, planetType, semiMajorAxis, eccentricity, inclination, orbitalPeriodYears, moonCount, hasRings, isHabitable));
        }

        return planets;
    }

    private static List<double> GenerateOrbitalDistances(Random random, int planetCount, GeneratedStarProfile star)
    {
        var innerLimit = Math.Max(0.15, 0.18 * Math.Sqrt(star.MassSolar));
        var outerLimit = Math.Max(innerLimit + 1.6, Math.Min(36.0, 18.0 * star.MassSolar));
        var distances = new List<double>(planetCount);

        for (var index = 0; index < planetCount; index++)
        {
            var progress = planetCount == 1 ? 0.5 : index / (double)(planetCount - 1);
            var baseDistance = innerLimit * Math.Pow(outerLimit / innerLimit, progress);
            var randomizedDistance = baseDistance * (0.82 + (random.NextDouble() * 0.36));
            distances.Add(randomizedDistance);
        }

        distances.Sort();
        for (var index = 1; index < distances.Count; index++)
        {
            if (distances[index] - distances[index - 1] < 0.28)
            {
                distances[index] = distances[index - 1] + 0.28;
            }
        }

        return distances;
    }

    private static string DeterminePlanetType(Random random, double orbitalDistanceAu, GeneratedStarProfile star, double eccentricity)
    {
        if (orbitalDistanceAu < 0.45)
        {
            return random.NextDouble() < 0.45 ? "Volcanic Super-Earth" : "Rocky Inner World";
        }

        if (orbitalDistanceAu >= star.HabitableZoneInnerAu && orbitalDistanceAu <= star.HabitableZoneOuterAu && eccentricity < 0.16)
        {
            var hzRoll = random.NextDouble();
            if (hzRoll < 0.45)
            {
                return "Temperate Terrestrial";
            }

            if (hzRoll < 0.70)
            {
                return "Oceanic Super-Earth";
            }

            return "Dry Terrestrial";
        }

        if (orbitalDistanceAu < star.HabitableZoneInnerAu)
        {
            return random.NextDouble() < 0.55 ? "Rocky Desert" : "Dense Super-Earth";
        }

        if (orbitalDistanceAu > 6.5)
        {
            return random.NextDouble() < 0.65 ? "Ice Giant" : "Cold Gas Giant";
        }

        return random.NextDouble() < 0.50 ? "Gas Giant" : "Ice World";
    }

    private static int GenerateMoonCount(Random random, string planetType)
    {
        if (planetType.Contains("Gas Giant", StringComparison.Ordinal))
        {
            return random.Next(10, 28);
        }

        if (planetType.Contains("Ice Giant", StringComparison.Ordinal))
        {
            return random.Next(7, 20);
        }

        if (planetType.Contains("Super-Earth", StringComparison.Ordinal))
        {
            return random.Next(1, 5);
        }

        return random.NextDouble() < 0.55 ? 0 : random.Next(1, 3);
    }

    private static bool IsHabitableCandidate(string planetType, double orbitalDistanceAu, GeneratedStarProfile star, double eccentricity)
    {
        var inBand = orbitalDistanceAu >= star.HabitableZoneInnerAu && orbitalDistanceAu <= star.HabitableZoneOuterAu;
        var terrestrial = planetType.Contains("Terrestrial", StringComparison.Ordinal) || planetType.Contains("Oceanic", StringComparison.Ordinal);
        return inBand && terrestrial && eccentricity <= 0.16;
    }

    private static string ToRomanNumeral(int value)
    {
        (int Value, string Glyph)[] map = [(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];
        var remaining = value;
        var builder = new StringBuilder();
        foreach (var (glyphValue, glyph) in map)
        {
            while (remaining >= glyphValue)
            {
                builder.Append(glyph);
                remaining -= glyphValue;
            }
        }

        return builder.ToString();
    }

    private static List<ReportedSolarSystem> BuildSystems(GeneratorConfig config, TerritoryRegionStructureData territory, out RouteAudit routeAudit)
    {
        var regionSectorMap = territory.RegionSectors.ToDictionary(item => item.Region.Index);
        var drafts = new Dictionary<int, SolarSystemDraft>();
        var sectorSystemIds = territory.RegionSectors
            .SelectMany(regionSectors => regionSectors.Sectors.Select(sector => ((regionSectors.Region.Index, sector.Index), new List<int>())))
            .ToDictionary(item => item.Item1, item => item.Item2);
        var nextId = 1;

        void AddDraft(SolarSystemDraft draft)
        {
            drafts.Add(draft.Id, draft);
            sectorSystemIds[(draft.RegionIndex, draft.SectorIndex)].Add(draft.Id);
        }

        foreach (var regionSectors in territory.RegionSectors.OrderBy(item => item.Region.Index))
        {
            for (var linkIndex = 0; linkIndex < regionSectors.GateLinks.Count; linkIndex++)
            {
                var link = regionSectors.GateLinks[linkIndex];
                var leftSystem = new SolarSystemDraft(
                    nextId++,
                    regionSectors.Region.Index,
                    link.SectorA,
                    link.SystemA,
                    "Medium Gate Anchor",
                    $"Medium gate {regionSectors.Region.Name}:{link.SectorA:D2}-{link.SectorB:D2} #{linkIndex + 1}",
                    new List<SolarSystemGateDraft>(),
                    ValidateSystem(territory, regionSectors.Region.Index, link.SectorA, link.SystemA));

                var rightSystem = new SolarSystemDraft(
                    nextId++,
                    regionSectors.Region.Index,
                    link.SectorB,
                    link.SystemB,
                    "Medium Gate Anchor",
                    $"Medium gate {regionSectors.Region.Name}:{link.SectorA:D2}-{link.SectorB:D2} #{linkIndex + 1}",
                    new List<SolarSystemGateDraft>(),
                    ValidateSystem(territory, regionSectors.Region.Index, link.SectorB, link.SystemB));

                leftSystem.Gates.Add(new SolarSystemGateDraft("Medium", link.DistanceLy, rightSystem.Id, $"{regionSectors.Region.Name}-S{link.SectorB:D2}"));
                rightSystem.Gates.Add(new SolarSystemGateDraft("Medium", link.DistanceLy, leftSystem.Id, $"{regionSectors.Region.Name}-S{link.SectorA:D2}"));
                AddDraft(leftSystem);
                AddDraft(rightSystem);
            }
        }

        for (var linkIndex = 0; linkIndex < territory.HeavyGateLinks.Count; linkIndex++)
        {
            var link = territory.HeavyGateLinks[linkIndex];
            var leftSector = LocateSectorIndex(regionSectorMap[link.RegionA], link.SystemA);
            var rightSector = LocateSectorIndex(regionSectorMap[link.RegionB], link.SystemB);

            var leftSystem = new SolarSystemDraft(
                nextId++,
                link.RegionA,
                leftSector,
                link.SystemA,
                "Heavy Gate Anchor",
                $"Heavy gate {territory.Regions[link.RegionA].Name}-{territory.Regions[link.RegionB].Name} #{linkIndex + 1}",
                new List<SolarSystemGateDraft>(),
                ValidateSystem(territory, link.RegionA, leftSector, link.SystemA));

            var rightSystem = new SolarSystemDraft(
                nextId++,
                link.RegionB,
                rightSector,
                link.SystemB,
                "Heavy Gate Anchor",
                $"Heavy gate {territory.Regions[link.RegionA].Name}-{territory.Regions[link.RegionB].Name} #{linkIndex + 1}",
                new List<SolarSystemGateDraft>(),
                ValidateSystem(territory, link.RegionB, rightSector, link.SystemB));

            leftSystem.Gates.Add(new SolarSystemGateDraft("Heavy", link.DistanceLy, rightSystem.Id, territory.Regions[link.RegionB].Name));
            rightSystem.Gates.Add(new SolarSystemGateDraft("Heavy", link.DistanceLy, leftSystem.Id, territory.Regions[link.RegionA].Name));
            AddDraft(leftSystem);
            AddDraft(rightSystem);
        }

        var randomSystemsBySector = AllocateRandomSystemsToSectors(config.StarCount, territory.RegionSectors);
        foreach (var regionSectors in territory.RegionSectors.OrderBy(item => item.Region.Index))
        {
            foreach (var sector in regionSectors.Sectors.OrderBy(item => item.Index))
            {
                var sectorKey = (regionSectors.Region.Index, sector.Index);
                var existingSystems = sectorSystemIds[sectorKey]
                    .Select(id => drafts[id])
                    .Select(draft => new ExistingSystemReference(draft.Id, draft.Position, draft.SourceType, draft.SourceDescription))
                    .ToList();
                var ownedPoints = regionSectors.OwnedSamples
                    .Where(sample => sample.OwnerIndex == sector.Index)
                    .Select(sample => sample.Position)
                    .ToList();

                var sectorPopulation = BuildSectorPopulation(
                    config,
                    territory,
                    regionSectors,
                    sector,
                    ownedPoints,
                    existingSystems,
                    randomSystemsBySector.GetValueOrDefault(sectorKey),
                    () => nextId++);

                foreach (var draft in sectorPopulation.NewSystems)
                {
                    AddDraft(draft);
                }

                foreach (var lightGate in sectorPopulation.LightGates)
                {
                    drafts[lightGate.SystemAId].Gates.Add(new SolarSystemGateDraft("Light", lightGate.DistanceLy, lightGate.SystemBId, lightGate.Scope));
                    drafts[lightGate.SystemBId].Gates.Add(new SolarSystemGateDraft("Light", lightGate.DistanceLy, lightGate.SystemAId, lightGate.Scope));
                }
            }
        }

        var orderedDrafts = drafts.Values
            .OrderBy(system => system.RegionIndex)
            .ThenBy(system => system.SectorIndex)
            .ThenBy(system => system.Position.X)
            .ThenBy(system => system.Position.Y)
            .ThenBy(system => system.Position.Z)
            .ToList();

        var profileById = BuildSystemProfiles(config, orderedDrafts);

        var sectorCounters = new Dictionary<(int RegionIndex, int SectorIndex), int>();
        var systems = new List<ReportedSolarSystem>(orderedDrafts.Count);
        var addressById = new Dictionary<int, string>();

        foreach (var draft in orderedDrafts)
        {
            var key = (draft.RegionIndex, draft.SectorIndex);
            var ordinal = sectorCounters.TryGetValue(key, out var current) ? current + 1 : 1;
            sectorCounters[key] = ordinal;
            var systemName = $"SYS{ordinal:D2}";
            var address = $"{BuildTrsPrefix(draft.RegionIndex, draft.SectorIndex)}-{systemName}";
            addressById[draft.Id] = address;
        }

        foreach (var draft in orderedDrafts)
        {
            var profile = profileById[draft.Id];
            var gateSummaries = draft.Gates
                .Select(gate => new SolarSystemGateSummary(
                    gate.GateType,
                    gate.DistanceLy,
                    addressById[gate.TargetSystemId],
                    gate.TargetScope))
                .OrderBy(gate => gate.GateType)
                .ThenBy(gate => gate.TargetAddress)
                .ToList();

            var checksPassed = draft.ValidationResult.HardChecks.All(check => check.Passed) && gateSummaries.All(gate => ValidateGateRange(gate.GateType, gate.DistanceLy));
            var gateCheckSummary = gateSummaries
                .Select(gate => new ValidationCheck($"{gate.GateType} gate range {gate.DistanceLy:0.00} ly", ValidateGateRange(gate.GateType, gate.DistanceLy)))
                .ToList();
            var heavyCount = gateSummaries.Count(gate => gate.GateType == "Heavy");
            var mediumCount = gateSummaries.Count(gate => gate.GateType == "Medium");
            var lightCount = gateSummaries.Count(gate => gate.GateType == "Light");

            systems.Add(new ReportedSolarSystem(
                draft.Id,
                draft.RegionIndex,
                draft.SectorIndex,
                profile.DisplayName,
                addressById[draft.Id],
                draft.Position,
                draft.SourceType,
                draft.SourceDescription,
                profile.StellarSummary,
                profile.ContentsSummary,
                profile.FlavorText,
                profile.Planets,
                gateSummaries,
                draft.ValidationResult.HardChecks.Concat(gateCheckSummary).ToList(),
                draft.ValidationResult.Warnings,
                checksPassed));
        }

        routeAudit = BuildRouteAudit(config, orderedDrafts, addressById);
        return systems;
    }

    private static ValidationResult ValidateSystem(TerritoryRegionStructureData territory, int regionIndex, int sectorIndex, Point3 position)
    {
        var hardChecks = new List<ValidationCheck>();
        var warnings = new List<ProjectionWarning>();
        var regionSectors = territory.RegionSectors.Single(item => item.Region.Index == regionIndex);
        var regionNuclei = territory.Regions.OrderBy(region => region.Index).Select(region => region.Nucleus).ToList();
        var sectorNuclei = regionSectors.Sectors.OrderBy(sector => sector.Index).Select(sector => sector.Nucleus).ToList();

        hardChecks.Add(new ValidationCheck("Inside territory bounds", IsInsideTerritory(territory.Span, position)));
        hardChecks.Add(new ValidationCheck($"Assigned to {territory.Regions[regionIndex].Name}", FindNearestIndex(position, regionNuclei) == regionIndex));
        hardChecks.Add(new ValidationCheck($"Assigned to sector S{sectorIndex:D2}", FindNearestIndex(position, sectorNuclei) == sectorIndex));

        warnings.Add(new ProjectionWarning("Top Down projection crosses sector boundary", FindNearestProjectedIndex(ProjectTopDown(position), sectorNuclei, ProjectTopDown) != sectorIndex));
        warnings.Add(new ProjectionWarning("Side projection crosses sector boundary", FindNearestProjectedIndex(ProjectSide(position), sectorNuclei, ProjectSide) != sectorIndex));
        warnings.Add(new ProjectionWarning("Angled projection crosses sector boundary", FindNearestProjectedIndex(ProjectAngled(position), sectorNuclei, ProjectAngled) != sectorIndex));
        return new ValidationResult(hardChecks, warnings.Where(warning => warning.IsTriggered).ToList());
    }

    private static int LocateSectorIndex(RegionSectorSet regionSectors, Point3 position)
    {
        var sectorNuclei = regionSectors.Sectors.OrderBy(sector => sector.Index).Select(sector => sector.Nucleus).ToList();
        return FindNearestIndex(position, sectorNuclei);
    }

    private static Dictionary<(int RegionIndex, int SectorIndex), int> AllocateRandomSystemsToSectors(int totalRandomSystems, IReadOnlyList<RegionSectorSet> regionSectors)
    {
        var sectorWeights = regionSectors
            .SelectMany(region => region.Sectors.Select(sector => new
            {
                Key = (region.Region.Index, sector.Index),
                Weight = Math.Max(1, region.OwnedSamples.Count(sample => sample.OwnerIndex == sector.Index))
            }))
            .ToList();

        var allocations = sectorWeights.ToDictionary(item => item.Key, _ => 0);
        if (totalRandomSystems <= 0 || sectorWeights.Count == 0)
        {
            return allocations;
        }

        var remaining = totalRandomSystems;
        if (remaining >= sectorWeights.Count)
        {
            foreach (var item in sectorWeights)
            {
                allocations[item.Key] = 1;
            }

            remaining -= sectorWeights.Count;
        }

        if (remaining <= 0)
        {
            return allocations;
        }

        var totalWeight = sectorWeights.Sum(item => item.Weight);
        var fractional = new List<((int RegionIndex, int SectorIndex) Key, double Fractional)>(sectorWeights.Count);
        foreach (var item in sectorWeights)
        {
            var exactShare = remaining * ((double)item.Weight / totalWeight);
            var floorShare = (int)Math.Floor(exactShare);
            allocations[item.Key] += floorShare;
            fractional.Add((item.Key, exactShare - floorShare));
            remaining -= floorShare;
        }

        foreach (var item in fractional.OrderByDescending(entry => entry.Fractional).ThenBy(entry => entry.Key.RegionIndex).ThenBy(entry => entry.Key.SectorIndex).Take(remaining))
        {
            allocations[item.Key]++;
        }

        return allocations;
    }

    private static SectorPopulationResult BuildSectorPopulation(
        GeneratorConfig config,
        TerritoryRegionStructureData territory,
        RegionSectorSet regionSectors,
        SectorCell sector,
        IReadOnlyList<Point3> ownedPoints,
        IReadOnlyList<ExistingSystemReference> existingSystems,
        int randomSystemCount,
        Func<int> allocateId)
    {
        var lightRange = config.MinimumStarDistanceLy * 3.0;
        var random = new Random(StableSeedHasher.HashToInt32($"{config.GeographySeed}:sector-pop:{regionSectors.Region.Index}:{sector.Index}"));
        var selectedRandomPoints = SelectRandomSectorSystems(ownedPoints, existingSystems, randomSystemCount, random);

        if (existingSystems.Count == 1 && selectedRandomPoints.Count == 0)
        {
            var connector = FindClosestDistinctPoint(ownedPoints, existingSystems[0].Position);
            if (connector is not null)
            {
                selectedRandomPoints.Add(connector.Value);
            }
        }

        var waypointIndexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var waypoints = new List<WaypointDraft>();
        var requiredWaypointIndices = new List<int>();

        int RegisterWaypoint(Point3 position, int? systemId, bool isRequired, string sourceType, string sourceDescription)
        {
            var key = CreatePointKey(position);
            if (waypointIndexByKey.TryGetValue(key, out var existingIndex))
            {
                return existingIndex;
            }

            var waypointIndex = waypoints.Count;
            waypoints.Add(new WaypointDraft(position, systemId, isRequired, sourceType, sourceDescription));
            waypointIndexByKey.Add(key, waypointIndex);
            return waypointIndex;
        }

        foreach (var existing in existingSystems)
        {
            requiredWaypointIndices.Add(RegisterWaypoint(existing.Position, existing.Id, true, existing.SourceType, existing.SourceDescription));
        }

        foreach (var point in selectedRandomPoints)
        {
            requiredWaypointIndices.Add(RegisterWaypoint(
                point,
                null,
                true,
                "Random Local System",
                $"Randomly placed within {regionSectors.Region.Name}-S{sector.Index:D2}"));
        }

        foreach (var point in ownedPoints)
        {
            RegisterWaypoint(
                point,
                null,
                false,
                "Light Network Connector",
                $"Connector inserted to preserve light-gate reachability within {regionSectors.Region.Name}-S{sector.Index:D2}");
        }

        requiredWaypointIndices = requiredWaypointIndices.Distinct().ToList();
        if (requiredWaypointIndices.Count == 0)
        {
            return new SectorPopulationResult(Array.Empty<SolarSystemDraft>(), Array.Empty<LightGateDraft>());
        }

        var graph = BuildWaypointGraph(waypoints, lightRange);
        var dijkstraBySource = requiredWaypointIndices.ToDictionary(index => index, index => RunDijkstra(index, graph));
        var mstPaths = BuildRequiredPaths(requiredWaypointIndices, dijkstraBySource);
        var systemIdByWaypoint = new Dictionary<int, int>();
        var newSystems = new List<SolarSystemDraft>();
        var lightGates = new List<LightGateDraft>();
        var lightGateKeys = new HashSet<(int Left, int Right)>();

        foreach (var waypointIndex in requiredWaypointIndices)
        {
            EnsureSystemForWaypoint(waypointIndex);
        }

        foreach (var path in mstPaths)
        {
            for (var step = 0; step < path.Count; step++)
            {
                EnsureSystemForWaypoint(path[step]);
                if (step == 0)
                {
                    continue;
                }

                var leftId = systemIdByWaypoint[path[step - 1]];
                var rightId = systemIdByWaypoint[path[step]];
                if (leftId == rightId)
                {
                    continue;
                }

                var edgeKey = leftId < rightId ? (leftId, rightId) : (rightId, leftId);
                if (!lightGateKeys.Add(edgeKey))
                {
                    continue;
                }

                var distanceLy = Distance(waypoints[path[step - 1]].Position, waypoints[path[step]].Position);
                if (distanceLy > lightRange + 0.0001)
                {
                    throw new InvalidOperationException($"Light gate in {regionSectors.Region.Name}-S{sector.Index:D2} exceeded range while building local network.");
                }

                lightGates.Add(new LightGateDraft(leftId, rightId, distanceLy, $"{regionSectors.Region.Name}-S{sector.Index:D2}"));
            }
        }

        ValidateSectorPopulation(requiredWaypointIndices, systemIdByWaypoint, lightGates, regionSectors.Region.Name, sector.Index);
        return new SectorPopulationResult(newSystems, lightGates);

        void EnsureSystemForWaypoint(int waypointIndex)
        {
            if (systemIdByWaypoint.ContainsKey(waypointIndex))
            {
                return;
            }

            var waypoint = waypoints[waypointIndex];
            if (waypoint.SystemId is not null)
            {
                systemIdByWaypoint[waypointIndex] = waypoint.SystemId.Value;
                return;
            }

            var draft = new SolarSystemDraft(
                allocateId(),
                regionSectors.Region.Index,
                sector.Index,
                waypoint.Position,
                waypoint.SourceType,
                waypoint.SourceDescription,
                new List<SolarSystemGateDraft>(),
                ValidateSystem(territory, regionSectors.Region.Index, sector.Index, waypoint.Position));
            systemIdByWaypoint[waypointIndex] = draft.Id;
            newSystems.Add(draft);
        }
    }

    private static List<Point3> SelectRandomSectorSystems(IReadOnlyList<Point3> ownedPoints, IReadOnlyList<ExistingSystemReference> existingSystems, int targetCount, Random random)
    {
        var selected = new List<Point3>();
        if (targetCount <= 0 || ownedPoints.Count == 0)
        {
            return selected;
        }

        var pool = ownedPoints
            .Where(point => existingSystems.All(existing => DistanceSquared(existing.Position, point) > 0.0001))
            .Distinct()
            .ToList();

        var references = existingSystems.Select(existing => existing.Position).ToList();
        while (selected.Count < targetCount && pool.Count > 0)
        {
            var bestIndex = -1;
            var bestScore = double.MinValue;
            for (var index = 0; index < pool.Count; index++)
            {
                var point = pool[index];
                var nearestReferenceDistance = references.Count == 0 ? 0.0 : references.Min(reference => DistanceSquared(reference, point));
                var score = nearestReferenceDistance + (random.NextDouble() * 0.05);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = index;
                }
            }

            var chosen = pool[bestIndex];
            selected.Add(chosen);
            references.Add(chosen);
            pool.RemoveAt(bestIndex);
        }

        return selected;
    }

    private static Point3? FindClosestDistinctPoint(IReadOnlyList<Point3> ownedPoints, Point3 origin)
    {
        Point3? best = null;
        var bestDistance = double.MaxValue;
        foreach (var point in ownedPoints)
        {
            var distance = Distance(origin, point);
            if (distance <= 0.0001)
            {
                continue;
            }

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = point;
            }
        }

        return best;
    }

    private static List<List<WaypointEdge>> BuildWaypointGraph(IReadOnlyList<WaypointDraft> waypoints, double maximumRange)
    {
        var graph = Enumerable.Range(0, waypoints.Count).Select(_ => new List<WaypointEdge>()).ToList();
        for (var leftIndex = 0; leftIndex < waypoints.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < waypoints.Count; rightIndex++)
            {
                var distanceLy = Distance(waypoints[leftIndex].Position, waypoints[rightIndex].Position);
                if (distanceLy <= 0.0001 || distanceLy > maximumRange + 0.0001)
                {
                    continue;
                }

                graph[leftIndex].Add(new WaypointEdge(rightIndex, distanceLy));
                graph[rightIndex].Add(new WaypointEdge(leftIndex, distanceLy));
            }
        }

        return graph;
    }

    private static DijkstraState RunDijkstra(int sourceIndex, IReadOnlyList<List<WaypointEdge>> graph)
    {
        var distances = Enumerable.Repeat(double.PositiveInfinity, graph.Count).ToArray();
        var previous = Enumerable.Repeat(-1, graph.Count).ToArray();
        var visited = new bool[graph.Count];
        distances[sourceIndex] = 0.0;

        for (var iteration = 0; iteration < graph.Count; iteration++)
        {
            var currentIndex = -1;
            var currentDistance = double.PositiveInfinity;
            for (var index = 0; index < graph.Count; index++)
            {
                if (!visited[index] && distances[index] < currentDistance)
                {
                    currentDistance = distances[index];
                    currentIndex = index;
                }
            }

            if (currentIndex < 0)
            {
                break;
            }

            visited[currentIndex] = true;
            foreach (var edge in graph[currentIndex])
            {
                var candidateDistance = distances[currentIndex] + edge.DistanceLy;
                if (candidateDistance + 0.0000001 < distances[edge.TargetIndex])
                {
                    distances[edge.TargetIndex] = candidateDistance;
                    previous[edge.TargetIndex] = currentIndex;
                }
            }
        }

        return new DijkstraState(distances, previous);
    }

    private static List<List<int>> BuildRequiredPaths(IReadOnlyList<int> requiredWaypointIndices, IReadOnlyDictionary<int, DijkstraState> dijkstraBySource)
    {
        var paths = new List<List<int>>();
        if (requiredWaypointIndices.Count <= 1)
        {
            return paths;
        }

        var connected = new HashSet<int> { requiredWaypointIndices[0] };
        while (connected.Count < requiredWaypointIndices.Count)
        {
            var bestSource = -1;
            var bestTarget = -1;
            var bestDistance = double.PositiveInfinity;

            foreach (var source in connected)
            {
                var state = dijkstraBySource[source];
                foreach (var target in requiredWaypointIndices)
                {
                    if (connected.Contains(target))
                    {
                        continue;
                    }

                    if (state.Distances[target] < bestDistance)
                    {
                        bestDistance = state.Distances[target];
                        bestSource = source;
                        bestTarget = target;
                    }
                }
            }

            if (bestSource < 0 || bestTarget < 0 || double.IsInfinity(bestDistance))
            {
                throw new InvalidOperationException("Unable to build a fully connected light-gate route across required systems inside a sector.");
            }

            paths.Add(ReconstructPath(bestTarget, dijkstraBySource[bestSource].Previous));
            connected.Add(bestTarget);
        }

        return paths;
    }

    private static List<int> ReconstructPath(int targetIndex, IReadOnlyList<int> previous)
    {
        var path = new List<int>();
        for (var current = targetIndex; current >= 0; current = previous[current])
        {
            path.Add(current);
        }

        path.Reverse();
        return path;
    }

    private static void ValidateSectorPopulation(IReadOnlyList<int> requiredWaypointIndices, IReadOnlyDictionary<int, int> systemIdByWaypoint, IReadOnlyList<LightGateDraft> lightGates, string regionName, int sectorIndex)
    {
        if (requiredWaypointIndices.Count <= 1)
        {
            return;
        }

        var adjacency = new Dictionary<int, List<int>>();
        foreach (var lightGate in lightGates)
        {
            if (!adjacency.TryGetValue(lightGate.SystemAId, out var leftNeighbors))
            {
                leftNeighbors = new List<int>();
                adjacency.Add(lightGate.SystemAId, leftNeighbors);
            }

            if (!adjacency.TryGetValue(lightGate.SystemBId, out var rightNeighbors))
            {
                rightNeighbors = new List<int>();
                adjacency.Add(lightGate.SystemBId, rightNeighbors);
            }

            leftNeighbors.Add(lightGate.SystemBId);
            rightNeighbors.Add(lightGate.SystemAId);
        }

        var requiredIds = requiredWaypointIndices.Select(index => systemIdByWaypoint[index]).Distinct().ToList();
        var visited = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(requiredIds[0]);
        visited.Add(requiredIds[0]);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!adjacency.TryGetValue(current, out var neighbors))
            {
                continue;
            }

            foreach (var neighbor in neighbors)
            {
                if (visited.Add(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        if (requiredIds.Any(id => !visited.Contains(id)))
        {
            throw new InvalidOperationException($"Sector light-gate network validation failed in {regionName}-S{sectorIndex:D2}.");
        }
    }

    private static RouteAudit BuildRouteAudit(GeneratorConfig config, IReadOnlyList<SolarSystemDraft> drafts, IReadOnlyDictionary<int, string> addressById)
    {
        if (drafts.Count == 0)
        {
            return new RouteAudit("N/A", 0, 0, 0, true, Array.Empty<string>(), Array.Empty<RoutePath>());
        }

        var random = new Random(StableSeedHasher.HashToInt32($"{config.HistorySeed}:route-audit"));
        var startDraft = drafts[random.Next(drafts.Count)];
        var draftById = drafts.ToDictionary(draft => draft.Id);
        var visited = new HashSet<int> { startDraft.Id };
        var queue = new Queue<int>();
        var previousById = new Dictionary<int, int?> { [startDraft.Id] = null };
        queue.Enqueue(startDraft.Id);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            foreach (var gate in draftById[currentId].Gates)
            {
                if (visited.Add(gate.TargetSystemId))
                {
                    previousById[gate.TargetSystemId] = currentId;
                    queue.Enqueue(gate.TargetSystemId);
                }
            }
        }

        var unreachable = drafts
            .Where(draft => !visited.Contains(draft.Id))
            .Select(draft => addressById[draft.Id])
            .ToList();

        var paths = drafts
            .Where(draft => draft.Id != startDraft.Id)
            .Where(draft => visited.Contains(draft.Id))
            .OrderBy(draft => draft.RegionIndex)
            .ThenBy(draft => draft.SectorIndex)
            .ThenBy(draft => addressById[draft.Id])
            .Select(draft => BuildRoutePath(startDraft.Id, draft.Id, previousById, draftById, addressById))
            .ToList();

        return new RouteAudit(addressById[startDraft.Id], startDraft.Id, visited.Count, drafts.Count, unreachable.Count == 0, unreachable, paths);
    }

    private static RoutePath BuildRoutePath(int startSystemId, int destinationSystemId, IReadOnlyDictionary<int, int?> previousById, IReadOnlyDictionary<int, SolarSystemDraft> draftById, IReadOnlyDictionary<int, string> addressById)
    {
        var systemIds = new List<int>();
        for (int? current = destinationSystemId; current is not null; current = previousById[current.Value])
        {
            systemIds.Add(current.Value);
        }

        systemIds.Reverse();

        var hops = new List<RouteHop>(Math.Max(0, systemIds.Count - 1));
        var totalDistance = 0.0;
        for (var index = 1; index < systemIds.Count; index++)
        {
            var fromId = systemIds[index - 1];
            var toId = systemIds[index];
            var gate = draftById[fromId].Gates.First(gateDraft => gateDraft.TargetSystemId == toId);
            totalDistance += gate.DistanceLy;
            hops.Add(new RouteHop(addressById[fromId], addressById[toId], gate.GateType, gate.DistanceLy, gate.TargetScope));
        }

        return new RoutePath(addressById[startSystemId], addressById[destinationSystemId], systemIds.Count - 1, totalDistance, hops);
    }

    private static string CreatePointKey(Point3 point) => $"{point.X:0.0000}|{point.Y:0.0000}|{point.Z:0.0000}";

    private static string SanitizeFileComponent(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '-' || character == '_' ? character : '_');
        }

        return builder.ToString();
    }

    private static double DistanceSquared(Point3 left, Point3 right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    private static double Distance(Point3 left, Point3 right) => Math.Sqrt(DistanceSquared(left, right));

    private static string Render(GeneratorConfig config, TerritoryRegionStructureData territory, IReadOnlyList<ReportedSolarSystem> systems, RouteAudit routeAudit)
    {
        var validCount = systems.Count(system => system.IsValid);
        var invalidCount = systems.Count - validCount;
        var totalGateLinks = systems.Sum(system => system.Gates.Count) / 2;
        var groupedSystems = systems
            .GroupBy(system => system.RegionIndex)
            .OrderBy(group => group.Key)
            .Select(regionGroup => new
            {
                RegionIndex = regionGroup.Key,
                RegionName = territory.Regions[regionGroup.Key].Name,
                Systems = regionGroup.OrderBy(system => system.Address).ToList(),
                SectorGroups = regionGroup
                    .GroupBy(system => system.SectorIndex)
                    .OrderBy(group => group.Key)
                    .Select(sectorGroup => new
                    {
                        SectorIndex = sectorGroup.Key,
                        SectorName = $"{territory.Regions[regionGroup.Key].Name}-S{sectorGroup.Key:D2}",
                        Systems = sectorGroup.OrderBy(system => system.Address).ToList()
                    })
                    .ToList()
            })
            .ToList();
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"UTF-8\" />");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />");
        builder.AppendLine($"  <title>{Escape(config.TerritoryName)} Solar Systems</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    body { margin: 0; padding: 28px; background: #07111f; color: #e6eef8; font-family: Consolas, 'Courier New', monospace; }");
        builder.AppendLine("    h1 { margin: 0 0 8px; font-size: 30px; font-weight: 500; }");
        builder.AppendLine("    h2 { margin: 28px 0 10px; font-size: 22px; font-weight: 500; }");
        builder.AppendLine("    h3 { margin: 20px 0 8px; font-size: 17px; font-weight: 500; color: #c8d9ec; }");
        builder.AppendLine("    .meta { color: #8fa6be; margin-bottom: 18px; }");
        builder.AppendLine("    .grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; margin-bottom: 24px; }");
        builder.AppendLine("    .card { background: rgba(12, 24, 38, 0.92); border: 1px solid #20364d; border-radius: 14px; padding: 14px 16px; }");
        builder.AppendLine("    .label { color: #8fa6be; font-size: 12px; text-transform: uppercase; letter-spacing: 0.08em; }");
        builder.AppendLine("    .value { font-size: 22px; margin-top: 6px; }");
        builder.AppendLine("    .browse-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: 12px; margin-bottom: 28px; }");
        builder.AppendLine("    .browse-card { background: rgba(12, 24, 38, 0.92); border: 1px solid #20364d; border-radius: 14px; padding: 14px 16px; }");
        builder.AppendLine("    .browse-links { display: flex; flex-wrap: wrap; gap: 8px 10px; margin-top: 12px; }");
        builder.AppendLine("    .browse-link { color: #9ed8ff; text-decoration: none; border-bottom: 1px solid transparent; }");
        builder.AppendLine("    .browse-link:hover { border-bottom-color: rgba(158, 216, 255, 0.65); }");
        builder.AppendLine("    .browse-count { color: #8fa6be; font-size: 12px; }");
        builder.AppendLine("    .region-section { margin-top: 28px; padding-top: 12px; border-top: 1px solid #1a3047; }");
        builder.AppendLine("    .sector-section { margin-top: 18px; padding: 14px 16px 6px; background: rgba(8, 18, 30, 0.68); border: 1px solid #17304a; border-radius: 16px; }");
        builder.AppendLine("    .section-header { display: flex; justify-content: space-between; align-items: baseline; gap: 12px; flex-wrap: wrap; }");
        builder.AppendLine("    .section-meta { color: #8fa6be; }");
        builder.AppendLine("    .jump-link { color: #9ed8ff; text-decoration: none; }");
        builder.AppendLine("    .jump-link:hover { text-decoration: underline; }");
        builder.AppendLine("    .system { background: rgba(12, 24, 38, 0.92); border: 1px solid #20364d; border-radius: 14px; padding: 14px 16px; margin-bottom: 12px; }");
        builder.AppendLine("    .system-header { display: flex; justify-content: space-between; gap: 12px; align-items: baseline; flex-wrap: wrap; }");
        builder.AppendLine("    .address { color: #f3f7fb; font-size: 18px; }");
        builder.AppendLine("    .system-name { color: #ffd38f; font-size: 20px; margin: 8px 0 4px; }");
        builder.AppendLine("    .status-ok { color: #8fd694; }");
        builder.AppendLine("    .status-bad { color: #ff8f8f; }");
        builder.AppendLine("    .subtle { color: #9db4ca; }");
        builder.AppendLine("    .mono { white-space: nowrap; }");
        builder.AppendLine("    table { width: 100%; border-collapse: collapse; margin-top: 10px; }");
        builder.AppendLine("    th, td { border-bottom: 1px solid #20364d; padding: 8px 10px; text-align: left; vertical-align: top; }");
        builder.AppendLine("    th { color: #9db4ca; font-size: 12px; text-transform: uppercase; letter-spacing: 0.08em; }");
        builder.AppendLine("    ul { margin: 8px 0 0 18px; padding: 0; }");
        builder.AppendLine("    li { margin: 4px 0; }");
        builder.AppendLine("    .orbit-note { color: #9ed8ff; margin-top: 10px; }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine($"  <h1 id=\"top\">{Escape(config.TerritoryName)} Solar System Validation Report</h1>");
        builder.AppendLine($"  <div class=\"meta\">Geography {Escape(config.GeographySeed)} | Systems derived from heavy, medium, and sector-local light gates | TRS address format T0R##S##-SYS## | Generated orbital profiles include eccentricity and inclination</div>");
        builder.AppendLine("  <div class=\"grid\">");
        builder.AppendLine($"    <div class=\"card\"><div class=\"label\">Solar Systems</div><div class=\"value\">{systems.Count}</div></div>");
        builder.AppendLine($"    <div class=\"card\"><div class=\"label\">Valid</div><div class=\"value\">{validCount}</div></div>");
        builder.AppendLine($"    <div class=\"card\"><div class=\"label\">Invalid</div><div class=\"value\">{invalidCount}</div></div>");
        builder.AppendLine($"    <div class=\"card\"><div class=\"label\">Star Gates</div><div class=\"value\">{totalGateLinks}</div></div>");
        builder.AppendLine("  </div>");
        builder.AppendLine($"  <div class=\"meta\">Random route audit start {Escape(routeAudit.StartAddress)} | reached {routeAudit.ReachableCount} of {routeAudit.TotalSystems} systems | {(routeAudit.Passed ? "PASS" : "FAIL")}</div>");
        if (!routeAudit.Passed)
        {
            builder.AppendLine("  <div class=\"system\">");
            builder.AppendLine("    <div class=\"subtle\">Unreachable Systems</div>");
            builder.AppendLine("    <ul>");
            foreach (var address in routeAudit.UnreachableAddresses)
            {
                builder.AppendLine($"      <li class=\"status-bad\">{Escape(address)}</li>");
            }
            builder.AppendLine("    </ul>");
            builder.AppendLine("  </div>");
        }

        builder.AppendLine("  <h2 id=\"browse\">Browse by Region and Sector</h2>");
        builder.AppendLine("  <div class=\"browse-grid\">");
        foreach (var regionGroup in groupedSystems)
        {
            var regionAnchor = $"region-{regionGroup.RegionName}";
            builder.AppendLine("    <div class=\"browse-card\">");
            builder.AppendLine("      <div class=\"label\">Region</div>");
            builder.AppendLine($"      <div class=\"value\"><a class=\"browse-link\" href=\"#{regionAnchor}\">{Escape(regionGroup.RegionName)}</a></div>");
            builder.AppendLine($"      <div class=\"section-meta\">{regionGroup.Systems.Count} system(s) across {regionGroup.SectorGroups.Count} sector(s)</div>");
            builder.AppendLine("      <div class=\"browse-links\">");
            foreach (var sectorGroup in regionGroup.SectorGroups)
            {
                var sectorAnchor = $"sector-{sectorGroup.SectorName}";
                builder.AppendLine($"        <a class=\"browse-link\" href=\"#{sectorAnchor}\">{Escape(sectorGroup.SectorName)}</a><span class=\"browse-count\">{sectorGroup.Systems.Count}</span>");
            }
            builder.AppendLine("      </div>");
            builder.AppendLine("    </div>");
        }
        builder.AppendLine("  </div>");

        foreach (var regionGroup in groupedSystems)
        {
            var regionAnchor = $"region-{regionGroup.RegionName}";
            builder.AppendLine($"  <section class=\"region-section\" id=\"{regionAnchor}\">");
            builder.AppendLine("    <div class=\"section-header\">");
            builder.AppendLine($"      <h2>{Escape(regionGroup.RegionName)}</h2>");
            builder.AppendLine($"      <div class=\"section-meta\">{regionGroup.Systems.Count} system(s) | {regionGroup.SectorGroups.Count} sector(s) | <a class=\"jump-link\" href=\"#browse\">browse index</a></div>");
            builder.AppendLine("    </div>");
            foreach (var sectorGroup in regionGroup.SectorGroups)
            {
                var sectorAnchor = $"sector-{sectorGroup.SectorName}";
                builder.AppendLine($"    <section class=\"sector-section\" id=\"{sectorAnchor}\">");
                builder.AppendLine("      <div class=\"section-header\">");
                builder.AppendLine($"        <h3>{Escape(sectorGroup.SectorName)}</h3>");
                builder.AppendLine($"        <div class=\"section-meta\">{sectorGroup.Systems.Count} system(s) | <a class=\"jump-link\" href=\"#{regionAnchor}\">back to {Escape(regionGroup.RegionName)}</a></div>");
                builder.AppendLine("      </div>");
                foreach (var system in sectorGroup.Systems)
                {
                    builder.AppendLine("      <div class=\"system\">");
                    builder.AppendLine("        <div class=\"system-header\">");
                    builder.AppendLine($"          <div class=\"address\">{Escape(system.Address)}</div>");
                    builder.AppendLine($"          <div class=\"{(system.IsValid ? "status-ok" : "status-bad")}\">{(system.IsValid ? "VALID" : "INVALID")}</div>");
                    builder.AppendLine("        </div>");
                    builder.AppendLine($"        <div class=\"system-name\">{Escape(system.Name)}</div>");
                    builder.AppendLine($"        <div class=\"subtle\">{Escape(system.SourceType)} | {Escape(system.SourceDescription)}</div>");
                    builder.AppendLine($"        <div class=\"subtle mono\">Position: ({system.Position.X:0.00}, {system.Position.Y:0.00}, {system.Position.Z:0.00})</div>");
                    builder.AppendLine($"        <div class=\"subtle\">{Escape(system.StellarSummary)}</div>");
                    builder.AppendLine($"        <p>{Escape(system.ContentsSummary)}</p>");
                    builder.AppendLine($"        <div class=\"subtle\">{Escape(system.FlavorText)}</div>");
                    builder.AppendLine("        <table>");
                    builder.AppendLine("          <thead><tr><th>Gate</th><th>Distance</th><th>Points To</th><th>Scope</th></tr></thead>");
                    builder.AppendLine("          <tbody>");
                    foreach (var gate in system.Gates)
                    {
                        builder.AppendLine($"            <tr><td>{Escape(gate.GateType)}</td><td class=\"mono\">{gate.DistanceLy:0.00} ly</td><td>{Escape(gate.TargetAddress)}</td><td>{Escape(gate.TargetScope)}</td></tr>");
                    }
                    builder.AppendLine("          </tbody>");
                    builder.AppendLine("        </table>");
                    builder.AppendLine("        <div class=\"orbit-note\">Orbit model uses elliptical semimajor axes with non-zero eccentricity and inclination; values below are not circular placeholders.</div>");
                    builder.AppendLine("        <table>");
                    builder.AppendLine("          <thead><tr><th>Planet</th><th>Type</th><th>Semi-Major Axis</th><th>Eccentricity</th><th>Inclination</th><th>Year</th><th>Moons</th><th>Rings</th><th>Habitable</th></tr></thead>");
                    builder.AppendLine("          <tbody>");
                    foreach (var planet in system.Planets)
                    {
                        builder.AppendLine($"            <tr><td>{Escape(planet.Name)}</td><td>{Escape(planet.PlanetType)}</td><td class=\"mono\">{planet.SemiMajorAxisAu:0.00} AU</td><td class=\"mono\">{planet.Eccentricity:0.000}</td><td class=\"mono\">{planet.InclinationDeg:0.0} deg</td><td class=\"mono\">{planet.OrbitalPeriodYears:0.00} yr</td><td>{planet.MoonCount}</td><td>{(planet.HasRings ? "Yes" : "No")}</td><td>{(planet.IsHabitable ? "Candidate" : "No")}</td></tr>");
                    }
                    builder.AppendLine("          </tbody>");
                    builder.AppendLine("        </table>");
                    builder.AppendLine("        <div class=\"subtle\">Diagnostic Checks</div>");
                    builder.AppendLine("        <ul>");
                    foreach (var check in system.ValidationChecks)
                    {
                        builder.AppendLine($"          <li class=\"{(check.Passed ? "status-ok" : "status-bad")}\">{Escape(check.Name)}: {(check.Passed ? "passed" : "failed")}</li>");
                    }
                    builder.AppendLine("        </ul>");
                    if (system.Warnings.Count > 0)
                    {
                        builder.AppendLine("        <div class=\"subtle\">Projection Warnings</div>");
                        builder.AppendLine("        <ul>");
                        foreach (var warning in system.Warnings)
                        {
                            builder.AppendLine($"          <li class=\"status-bad\">{Escape(warning.Name)}</li>");
                        }
                        builder.AppendLine("        </ul>");
                    }
                    builder.AppendLine("      </div>");
                }
                builder.AppendLine("    </section>");
            }
            builder.AppendLine("  </section>");
        }

        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static string RenderPathReport(GeneratorConfig config, IReadOnlyList<ReportedSolarSystem> systems, RouteAudit routeAudit)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"UTF-8\" />");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />");
        builder.AppendLine($"  <title>{Escape(config.TerritoryName)} Paths From {Escape(routeAudit.StartAddress)}</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    body { margin: 0; padding: 28px; background: #07111f; color: #e6eef8; font-family: Consolas, 'Courier New', monospace; }");
        builder.AppendLine("    h1 { margin: 0 0 8px; font-size: 30px; font-weight: 500; }");
        builder.AppendLine("    h2 { margin: 28px 0 10px; font-size: 20px; font-weight: 500; }");
        builder.AppendLine("    .meta { color: #8fa6be; margin-bottom: 18px; }");
        builder.AppendLine("    .grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; margin-bottom: 24px; }");
        builder.AppendLine("    .card { background: rgba(12, 24, 38, 0.92); border: 1px solid #20364d; border-radius: 14px; padding: 14px 16px; }");
        builder.AppendLine("    .label { color: #8fa6be; font-size: 12px; text-transform: uppercase; letter-spacing: 0.08em; }");
        builder.AppendLine("    .value { font-size: 22px; margin-top: 6px; }");
        builder.AppendLine("    .route { background: rgba(12, 24, 38, 0.92); border: 1px solid #20364d; border-radius: 14px; padding: 14px 16px; margin-bottom: 12px; }");
        builder.AppendLine("    .route-header { display: flex; justify-content: space-between; gap: 12px; align-items: baseline; flex-wrap: wrap; }");
        builder.AppendLine("    .address { color: #f3f7fb; font-size: 18px; }");
        builder.AppendLine("    .status-ok { color: #8fd694; }");
        builder.AppendLine("    table { width: 100%; border-collapse: collapse; margin-top: 10px; }");
        builder.AppendLine("    th, td { border-bottom: 1px solid #20364d; padding: 8px 10px; text-align: left; vertical-align: top; }");
        builder.AppendLine("    th { color: #9db4ca; font-size: 12px; text-transform: uppercase; letter-spacing: 0.08em; }");
        builder.AppendLine("    .mono { white-space: nowrap; }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine($"  <h1>{Escape(config.TerritoryName)} Path Validation From {Escape(routeAudit.StartAddress)}</h1>");
        builder.AppendLine($"  <div class=\"meta\">Origin {Escape(routeAudit.StartAddress)} | Reachable {routeAudit.ReachableCount} of {routeAudit.TotalSystems} | {(routeAudit.Passed ? "PASS" : "FAIL")}</div>");
        builder.AppendLine("  <div class=\"grid\">");
        builder.AppendLine($"    <div class=\"card\"><div class=\"label\">Origin</div><div class=\"value\">{Escape(routeAudit.StartAddress)}</div></div>");
        builder.AppendLine($"    <div class=\"card\"><div class=\"label\">Reachable</div><div class=\"value\">{routeAudit.ReachableCount}</div></div>");
        builder.AppendLine($"    <div class=\"card\"><div class=\"label\">Destinations</div><div class=\"value\">{routeAudit.Paths.Count}</div></div>");
        builder.AppendLine($"    <div class=\"card\"><div class=\"label\">Status</div><div class=\"value status-ok\">{(routeAudit.Passed ? "PASS" : "FAIL")}</div></div>");
        builder.AppendLine("  </div>");

        foreach (var path in routeAudit.Paths)
        {
            builder.AppendLine("  <div class=\"route\">");
            builder.AppendLine("    <div class=\"route-header\">");
            builder.AppendLine($"      <div class=\"address\">{Escape(path.DestinationAddress)}</div>");
            builder.AppendLine($"      <div>{path.HopCount} hop(s) | {path.TotalDistanceLy:0.00} ly</div>");
            builder.AppendLine("    </div>");
            builder.AppendLine($"    <div class=\"meta\">{Escape(path.StartAddress)} -> {Escape(path.DestinationAddress)}</div>");
            builder.AppendLine("    <table>");
            builder.AppendLine("      <thead><tr><th>Step</th><th>From</th><th>To</th><th>Gate</th><th>Distance</th><th>Scope</th></tr></thead>");
            builder.AppendLine("      <tbody>");
            for (var index = 0; index < path.Hops.Count; index++)
            {
                var hop = path.Hops[index];
                builder.AppendLine($"        <tr><td>{index + 1}</td><td>{Escape(hop.FromAddress)}</td><td>{Escape(hop.ToAddress)}</td><td>{Escape(hop.GateType)}</td><td class=\"mono\">{hop.DistanceLy:0.00} ly</td><td>{Escape(hop.Scope)}</td></tr>");
            }
            builder.AppendLine("      </tbody>");
            builder.AppendLine("    </table>");
            builder.AppendLine("  </div>");
        }

        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static bool ValidateGateRange(string gateType, double distanceLy)
    {
        var maximum = gateType.Equals("Heavy", StringComparison.OrdinalIgnoreCase)
            ? 48.0
            : gateType.Equals("Medium", StringComparison.OrdinalIgnoreCase)
                ? 24.0
                : 12.0;
        return distanceLy > 0.0 && distanceLy <= maximum + 0.0001;
    }

    private static bool IsInsideTerritory(Span3 span, Point3 point)
    {
        var halfWidth = span.X / 2.0;
        var halfHeight = span.Y / 2.0;
        var halfDepth = span.Z / 2.0;
        var normalized =
            (point.X * point.X) / (halfWidth * halfWidth) +
            (point.Y * point.Y) / (halfHeight * halfHeight) +
            (point.Z * point.Z) / (halfDepth * halfDepth);
        return normalized <= 1.0000001;
    }

    private static int FindNearestIndex(Point3 point, IReadOnlyList<Point3> nuclei)
    {
        var nearestIndex = 0;
        var nearestDistanceSquared = double.MaxValue;
        for (var index = 0; index < nuclei.Count; index++)
        {
            var dx = point.X - nuclei[index].X;
            var dy = point.Y - nuclei[index].Y;
            var dz = point.Z - nuclei[index].Z;
            var distanceSquared = (dx * dx) + (dy * dy) + (dz * dz);
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestIndex = index;
            }
        }

        return nearestIndex;
    }

    private static int FindNearestProjectedIndex(Point2 projectedPoint, IReadOnlyList<Point3> nuclei, Func<Point3, Point2> projector)
    {
        var nearestIndex = 0;
        var nearestDistanceSquared = double.MaxValue;
        for (var index = 0; index < nuclei.Count; index++)
        {
            var projected = projector(nuclei[index]);
            var dx = projectedPoint.X - projected.X;
            var dy = projectedPoint.Y - projected.Y;
            var distanceSquared = (dx * dx) + (dy * dy);
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestIndex = index;
            }
        }

        return nearestIndex;
    }

    private static Point2 ProjectTopDown(Point3 point) => new(point.X, -point.Y);
    private static Point2 ProjectSide(Point3 point) => new(point.X, -point.Z);
    private static Point2 ProjectAngled(Point3 point)
    {
        var x = (point.X - point.Y) * 0.8660254038;
        var y = ((point.X + point.Y) * 0.5) - (point.Z * 1.2);
        return new Point2(x, -y);
    }

    private static string Escape(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    private sealed record SolarSystemDraft(
        int Id,
        int RegionIndex,
        int SectorIndex,
        Point3 Position,
        string SourceType,
        string SourceDescription,
        List<SolarSystemGateDraft> Gates,
        ValidationResult ValidationResult);

    private sealed record SolarSystemGateDraft(string GateType, double DistanceLy, int TargetSystemId, string TargetScope);
    private sealed record ReportedSolarSystem(
        int Id,
        int RegionIndex,
        int SectorIndex,
        string Name,
        string Address,
        Point3 Position,
        string SourceType,
        string SourceDescription,
        string StellarSummary,
        string ContentsSummary,
        string FlavorText,
        IReadOnlyList<GeneratedPlanetProfile> Planets,
        IReadOnlyList<SolarSystemGateSummary> Gates,
        IReadOnlyList<ValidationCheck> ValidationChecks,
        IReadOnlyList<ProjectionWarning> Warnings,
        bool IsValid);

    private sealed record SolarSystemGateSummary(string GateType, double DistanceLy, string TargetAddress, string TargetScope);
    private sealed record GeneratedStarProfile(string StarName, string Classification, double MassSolar, double TemperatureK, double LuminositySolar, double AgeBillionYears, double HabitableZoneInnerAu, double HabitableZoneOuterAu);
    private sealed record GeneratedPlanetProfile(string Name, string PlanetType, double SemiMajorAxisAu, double Eccentricity, double InclinationDeg, double OrbitalPeriodYears, int MoonCount, bool HasRings, bool IsHabitable);
    private sealed record GeneratedSystemProfile(string DisplayName, string StellarSummary, string ContentsSummary, string FlavorText, IReadOnlyList<GeneratedPlanetProfile> Planets);
    private sealed record ValidationCheck(string Name, bool Passed);
    private sealed record ProjectionWarning(string Name, bool IsTriggered);
    private sealed record ValidationResult(IReadOnlyList<ValidationCheck> HardChecks, IReadOnlyList<ProjectionWarning> Warnings);
    private sealed record ExistingSystemReference(int Id, Point3 Position, string SourceType, string SourceDescription);
    private sealed record WaypointDraft(Point3 Position, int? SystemId, bool IsRequired, string SourceType, string SourceDescription);
    private sealed record WaypointEdge(int TargetIndex, double DistanceLy);
    private sealed record DijkstraState(IReadOnlyList<double> Distances, IReadOnlyList<int> Previous);
    private sealed record LightGateDraft(int SystemAId, int SystemBId, double DistanceLy, string Scope);
    private sealed record SectorPopulationResult(IReadOnlyList<SolarSystemDraft> NewSystems, IReadOnlyList<LightGateDraft> LightGates);
    private sealed record RouteHop(string FromAddress, string ToAddress, string GateType, double DistanceLy, string Scope);
    private sealed record RoutePath(string StartAddress, string DestinationAddress, int HopCount, double TotalDistanceLy, IReadOnlyList<RouteHop> Hops);
    private sealed record RouteAudit(string StartAddress, int StartSystemId, int ReachableCount, int TotalSystems, bool Passed, IReadOnlyList<string> UnreachableAddresses, IReadOnlyList<RoutePath> Paths);
}

static class RegionHeavyGateGenerator
{
    private const int MinimumAdjacentLinksPerRegion = 2;
    private const int MinimumAdditionalLinksPerRegion = 2;
    private const int MaximumAdditionalLinksPerRegion = 4;
    private const int MaximumLinksPerRegion = MinimumAdjacentLinksPerRegion + MaximumAdditionalLinksPerRegion;
    private const double AdjacencyBoundaryThreshold = 0.75;
    private const double BoundaryInsetEpsilon = 0.05;

    public static IReadOnlyList<HeavyGateLink> Generate(GeneratorConfig config, Span3 span, IReadOnlyList<RegionCell> regions, Random random)
    {
        var heavyRange = config.MinimumStarDistanceLy * 12.0;
        var minimumPreferredRange = heavyRange * 0.75;
        var targetTotals = regions.ToDictionary(region => region.Index, _ => MinimumAdjacentLinksPerRegion + random.Next(MinimumAdditionalLinksPerRegion, MaximumAdditionalLinksPerRegion + 1));
        var candidates = BuildCandidates(regions, span, heavyRange, minimumPreferredRange, random);
        var selected = new List<HeavyGateLink>();
        var degrees = regions.ToDictionary(region => region.Index, _ => 0);
        var adjacentDegrees = regions.ToDictionary(region => region.Index, _ => 0);

        FillMandatoryAdjacentLinks(regions, candidates, selected, degrees, adjacentDegrees);
        FillAdditionalLinks(regions, candidates, selected, degrees, adjacentDegrees, targetTotals);

        return selected;
    }

    private static void FillMandatoryAdjacentLinks(
        IReadOnlyList<RegionCell> regions,
        IReadOnlyList<HeavyGateCandidate> candidates,
        List<HeavyGateLink> selected,
        Dictionary<int, int> degrees,
        Dictionary<int, int> adjacentDegrees)
    {
        var progress = true;
        while (progress)
        {
            progress = false;
            foreach (var region in regions.OrderBy(region => adjacentDegrees[region.Index]).ThenBy(region => degrees[region.Index]).ThenBy(region => region.Index))
            {
                if (adjacentDegrees[region.Index] >= MinimumAdjacentLinksPerRegion)
                {
                    continue;
                }

                var choice = candidates
                    .Where(candidate => candidate.IsAdjacent)
                    .Where(candidate => candidate.RegionA == region.Index || candidate.RegionB == region.Index)
                    .Where(candidate => !ContainsLink(selected, candidate.RegionA, candidate.RegionB))
                    .Where(candidate => degrees[candidate.RegionA] < MaximumLinksPerRegion && degrees[candidate.RegionB] < MaximumLinksPerRegion)
                    .OrderByDescending(candidate => ScoreCandidate(candidate, selected, region.Index, preferAdjacencyNeed: true, adjacentDegrees))
                    .FirstOrDefault();

                if (choice is null)
                {
                    continue;
                }

                AddLink(selected, degrees, adjacentDegrees, choice);
                progress = true;
            }
        }
    }

    private static void FillAdditionalLinks(
        IReadOnlyList<RegionCell> regions,
        IReadOnlyList<HeavyGateCandidate> candidates,
        List<HeavyGateLink> selected,
        Dictionary<int, int> degrees,
        Dictionary<int, int> adjacentDegrees,
        Dictionary<int, int> targetTotals)
    {
        var progress = true;
        while (progress)
        {
            progress = false;
            foreach (var region in regions.OrderBy(region => degrees[region.Index]).ThenBy(region => region.Index))
            {
                if (degrees[region.Index] >= targetTotals[region.Index])
                {
                    continue;
                }

                var choice = candidates
                    .Where(candidate => candidate.RegionA == region.Index || candidate.RegionB == region.Index)
                    .Where(candidate => !ContainsLink(selected, candidate.RegionA, candidate.RegionB))
                    .Where(candidate => degrees[candidate.RegionA] < targetTotals[candidate.RegionA] && degrees[candidate.RegionB] < targetTotals[candidate.RegionB])
                    .OrderByDescending(candidate => ScoreCandidate(candidate, selected, region.Index, preferAdjacencyNeed: false, adjacentDegrees))
                    .FirstOrDefault();

                if (choice is null)
                {
                    choice = candidates
                        .Where(candidate => candidate.RegionA == region.Index || candidate.RegionB == region.Index)
                        .Where(candidate => !ContainsLink(selected, candidate.RegionA, candidate.RegionB))
                        .Where(candidate => degrees[candidate.RegionA] < MaximumLinksPerRegion && degrees[candidate.RegionB] < MaximumLinksPerRegion)
                        .OrderByDescending(candidate => ScoreCandidate(candidate, selected, region.Index, preferAdjacencyNeed: false, adjacentDegrees))
                        .FirstOrDefault();
                }

                if (choice is null)
                {
                    continue;
                }

                AddLink(selected, degrees, adjacentDegrees, choice);
                progress = true;
            }
        }
    }

    private static void AddLink(List<HeavyGateLink> selected, Dictionary<int, int> degrees, Dictionary<int, int> adjacentDegrees, HeavyGateCandidate candidate)
    {
        selected.Add(candidate.ToLink());
        degrees[candidate.RegionA]++;
        degrees[candidate.RegionB]++;
        if (candidate.IsAdjacent)
        {
            adjacentDegrees[candidate.RegionA]++;
            adjacentDegrees[candidate.RegionB]++;
        }
    }

    private static List<HeavyGateCandidate> BuildCandidates(IReadOnlyList<RegionCell> regions, Span3 span, double heavyRange, double minimumPreferredRange, Random random)
    {
        var nuclei = regions.OrderBy(region => region.Index).Select(region => region.Nucleus).ToList();
        var candidates = new List<HeavyGateCandidate>();

        for (var leftIndex = 0; leftIndex < regions.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < regions.Count; rightIndex++)
            {
                var left = regions[leftIndex];
                var right = regions[rightIndex];
                var direction = Normalize(Subtract(right.Nucleus, left.Nucleus));
                var leftBoundary = FindBoundaryPoint(span, left.Index, left.Nucleus, direction, nuclei);
                var rightBoundary = FindBoundaryPoint(span, right.Index, right.Nucleus, Negate(direction), nuclei);
                var boundaryGap = Distance(leftBoundary, rightBoundary);
                var isAdjacent = boundaryGap <= AdjacencyBoundaryThreshold;
                var bestCandidate = TryBuildCandidate(
                    span,
                    minimumPreferredRange,
                    heavyRange,
                    random,
                    nuclei,
                    left,
                    right,
                    leftBoundary,
                    rightBoundary,
                    direction,
                    boundaryGap,
                    isAdjacent);

                if (bestCandidate is not null)
                {
                    candidates.Add(bestCandidate);
                }
            }
        }

        return candidates;
    }

    private static HeavyGateCandidate? TryBuildCandidate(
        Span3 span,
        double minimumPreferredRange,
        double heavyRange,
        Random random,
        IReadOnlyList<Point3> nuclei,
        RegionCell left,
        RegionCell right,
        Point3 leftBoundary,
        Point3 rightBoundary,
        Point3 corridorDirection,
        double boundaryGap,
        bool isAdjacent)
    {
        HeavyGateCandidate? bestCandidate = null;
        var (basisA, basisB) = BuildPerpendicularBasis(corridorDirection);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var leftTravelDirection = BuildPlacementDirection(Negate(corridorDirection), basisA, basisB, random, 0.42, 0.26);
            var rightTravelDirection = BuildPlacementDirection(corridorDirection, basisA, basisB, random, 0.42, 0.26);

            var inwardCapacityLeft = FindInwardCapacity(span, left.Index, ShiftInside(leftBoundary, leftTravelDirection), leftTravelDirection, nuclei);
            var inwardCapacityRight = FindInwardCapacity(span, right.Index, ShiftInside(rightBoundary, rightTravelDirection), rightTravelDirection, nuclei);
            var maxReachableDistance = Math.Min(heavyRange, boundaryGap + inwardCapacityLeft + inwardCapacityRight);
            if (maxReachableDistance < minimumPreferredRange)
            {
                continue;
            }

            var distanceBand = maxReachableDistance - minimumPreferredRange;
            var targetDistance = maxReachableDistance - (distanceBand * (random.NextDouble() * 0.35));
            var requiredInset = Math.Max(0.0, targetDistance - boundaryGap);
            var split = 0.30 + (random.NextDouble() * 0.40);
            var leftInset = Math.Min(inwardCapacityLeft, requiredInset * split);
            var rightInset = Math.Min(inwardCapacityRight, requiredInset - leftInset);
            var remainingInset = requiredInset - leftInset - rightInset;

            if (remainingInset > 0.001)
            {
                var leftExtra = Math.Min(inwardCapacityLeft - leftInset, remainingInset);
                leftInset += Math.Max(0.0, leftExtra);
                remainingInset -= Math.Max(0.0, leftExtra);
            }

            if (remainingInset > 0.001)
            {
                var rightExtra = Math.Min(inwardCapacityRight - rightInset, remainingInset);
                rightInset += Math.Max(0.0, rightExtra);
                remainingInset -= Math.Max(0.0, rightExtra);
            }

            if (remainingInset > 0.25)
            {
                continue;
            }

            var systemA = Add(leftBoundary, Multiply(leftTravelDirection, leftInset));
            var systemB = Add(rightBoundary, Multiply(rightTravelDirection, rightInset));
            var systemDistance = Distance(systemA, systemB);
            if (systemDistance < minimumPreferredRange || systemDistance > heavyRange)
            {
                continue;
            }

            var asymmetry = Math.Abs(leftInset - rightInset);
            var angularSpread = AngleDegrees(Negate(leftTravelDirection), rightTravelDirection);
            var weight =
                (isAdjacent ? 100.0 : 0.0) +
                (systemDistance * 2.0) -
                (boundaryGap * 0.35) +
                (asymmetry * 0.80) +
                (angularSpread * 0.12) +
                (random.NextDouble() * 0.25);

            var candidate = new HeavyGateCandidate(left.Index, right.Index, systemA, systemB, systemDistance, isAdjacent, weight);
            if (bestCandidate is null || candidate.SelectionWeight > bestCandidate.SelectionWeight)
            {
                bestCandidate = candidate;
            }
        }

        return bestCandidate;
    }

    private static Point3 ShiftInside(Point3 boundary, Point3 inwardDirection)
    {
        return Add(boundary, Multiply(inwardDirection, BoundaryInsetEpsilon));
    }

    private static Point3 BuildPlacementDirection(Point3 inwardDirection, Point3 basisA, Point3 basisB, Random random, double maxBasisAWeight, double maxBasisBWeight)
    {
        var blendA = (random.NextDouble() * 2.0 - 1.0) * maxBasisAWeight;
        var blendB = (random.NextDouble() * 2.0 - 1.0) * maxBasisBWeight;
        return Normalize(Add(inwardDirection, Add(Multiply(basisA, blendA), Multiply(basisB, blendB))));
    }

    private static (Point3 BasisA, Point3 BasisB) BuildPerpendicularBasis(Point3 direction)
    {
        var reference = Math.Abs(direction.Z) < 0.82
            ? new Point3(0, 0, 1)
            : new Point3(0, 1, 0);
        var basisA = Normalize(Cross(direction, reference));
        var basisB = Normalize(Cross(direction, basisA));
        return (basisA, basisB);
    }

    private static bool ContainsLink(IReadOnlyList<HeavyGateLink> selected, int regionA, int regionB)
    {
        return selected.Any(link =>
            (link.RegionA == regionA && link.RegionB == regionB) ||
            (link.RegionA == regionB && link.RegionB == regionA));
    }

    private static double ScoreCandidate(HeavyGateCandidate candidate, IReadOnlyList<HeavyGateLink> selected, int focusRegion, bool preferAdjacencyNeed, IReadOnlyDictionary<int, int> adjacentDegrees)
    {
        var direction = candidate.RegionA == focusRegion
            ? Normalize(Subtract(candidate.SystemB, candidate.SystemA))
            : Normalize(Subtract(candidate.SystemA, candidate.SystemB));

        var existingDirections = selected
            .Where(link => link.RegionA == focusRegion || link.RegionB == focusRegion)
            .Select(link => link.RegionA == focusRegion
                ? Normalize(Subtract(link.SystemB, link.SystemA))
                : Normalize(Subtract(link.SystemA, link.SystemB)))
            .ToList();

        var spacingScore = existingDirections.Count == 0
            ? 180.0
            : existingDirections.Min(existing => AngleDegrees(existing, direction));

        var otherRegion = candidate.RegionA == focusRegion ? candidate.RegionB : candidate.RegionA;
        var adjacencyNeedBonus = preferAdjacencyNeed && candidate.IsAdjacent
            ? ((MinimumAdjacentLinksPerRegion - adjacentDegrees[focusRegion]) * 40.0) + ((MinimumAdjacentLinksPerRegion - adjacentDegrees[otherRegion]) * 20.0)
            : 0.0;

        return candidate.SelectionWeight + spacingScore + adjacencyNeedBonus;
    }

    private static double AngleDegrees(Point3 left, Point3 right)
    {
        var dot = Math.Clamp((left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z), -1.0, 1.0);
        return Math.Acos(dot) * (180.0 / Math.PI);
    }

    private static Point3 FindBoundaryPoint(Span3 span, int regionIndex, Point3 origin, Point3 direction, IReadOnlyList<Point3> nuclei)
    {
        var exitDistance = FindEllipsoidExitDistance(span, origin, direction);
        var low = 0.0;
        var high = exitDistance;

        for (var iteration = 0; iteration < 28; iteration++)
        {
            var midpoint = (low + high) * 0.5;
            var sample = Add(origin, Multiply(direction, midpoint));
            if (IsInsideTerritory(span, sample) && FindNearestRegionIndex(sample, nuclei) == regionIndex)
            {
                low = midpoint;
            }
            else
            {
                high = midpoint;
            }
        }

        return Add(origin, Multiply(direction, low));
    }

    private static double FindInwardCapacity(Span3 span, int regionIndex, Point3 origin, Point3 direction, IReadOnlyList<Point3> nuclei)
    {
        var exitDistance = FindEllipsoidExitDistance(span, origin, direction);
        var low = 0.0;
        var high = exitDistance;

        for (var iteration = 0; iteration < 28; iteration++)
        {
            var midpoint = (low + high) * 0.5;
            var sample = Add(origin, Multiply(direction, midpoint));
            if (IsInsideTerritory(span, sample) && FindNearestRegionIndex(sample, nuclei) == regionIndex)
            {
                low = midpoint;
            }
            else
            {
                high = midpoint;
            }
        }

        return low;
    }

    private static int FindNearestRegionIndex(Point3 sample, IReadOnlyList<Point3> nuclei)
    {
        var nearestIndex = 0;
        var nearestDistanceSquared = double.MaxValue;

        for (var index = 0; index < nuclei.Count; index++)
        {
            var distanceSquared = DistanceSquared(sample, nuclei[index]);
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestIndex = index;
            }
        }

        return nearestIndex;
    }

    private static double FindEllipsoidExitDistance(Span3 span, Point3 origin, Point3 direction)
    {
        var halfWidth = span.X / 2.0;
        var halfHeight = span.Y / 2.0;
        var halfDepth = span.Z / 2.0;
        var a = ((direction.X * direction.X) / (halfWidth * halfWidth)) + ((direction.Y * direction.Y) / (halfHeight * halfHeight)) + ((direction.Z * direction.Z) / (halfDepth * halfDepth));
        var b = 2.0 * (((origin.X * direction.X) / (halfWidth * halfWidth)) + ((origin.Y * direction.Y) / (halfHeight * halfHeight)) + ((origin.Z * direction.Z) / (halfDepth * halfDepth)));
        var c = ((origin.X * origin.X) / (halfWidth * halfWidth)) + ((origin.Y * origin.Y) / (halfHeight * halfHeight)) + ((origin.Z * origin.Z) / (halfDepth * halfDepth)) - 1.0;
        var discriminant = Math.Max(0.0, (b * b) - (4.0 * a * c));
        return (-b + Math.Sqrt(discriminant)) / (2.0 * a);
    }

    private static bool IsInsideTerritory(Span3 span, Point3 point)
    {
        var halfWidth = span.X / 2.0;
        var halfHeight = span.Y / 2.0;
        var halfDepth = span.Z / 2.0;
        var normalized =
            (point.X * point.X) / (halfWidth * halfWidth) +
            (point.Y * point.Y) / (halfHeight * halfHeight) +
            (point.Z * point.Z) / (halfDepth * halfDepth);
        return normalized <= 1.0000001;
    }

    private static double DistanceSquared(Point3 left, Point3 right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    private static double Distance(Point3 left, Point3 right) => Math.Sqrt(DistanceSquared(left, right));
    private static Point3 Add(Point3 left, Point3 right) => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    private static Point3 Subtract(Point3 left, Point3 right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    private static Point3 Multiply(Point3 point, double scalar) => new(point.X * scalar, point.Y * scalar, point.Z * scalar);
    private static Point3 Negate(Point3 point) => new(-point.X, -point.Y, -point.Z);
    private static Point3 Cross(Point3 left, Point3 right) => new(
        (left.Y * right.Z) - (left.Z * right.Y),
        (left.Z * right.X) - (left.X * right.Z),
        (left.X * right.Y) - (left.Y * right.X));

    private static Point3 Normalize(Point3 point)
    {
        var length = Math.Sqrt((point.X * point.X) + (point.Y * point.Y) + (point.Z * point.Z));
        if (length <= 0.0)
        {
            return new Point3(1, 0, 0);
        }

        return new Point3(point.X / length, point.Y / length, point.Z / length);
    }

    private sealed record HeavyGateCandidate(int RegionA, int RegionB, Point3 SystemA, Point3 SystemB, double SystemDistanceLy, bool IsAdjacent, double SelectionWeight)
    {
        public HeavyGateLink ToLink() => new(RegionA, RegionB, SystemA, SystemB, SystemDistanceLy, IsAdjacent);
    }
}

static class RegionSectorGateGenerator
{
    private const int MinimumLinksPerSector = 2;
    private const int MaximumLinksPerSector = 3;
    private const int BoundaryEvidencePerSide = 10;
    private const int AnchorCandidatesPerSide = 20;
    private const double SectorWallMarginRatio = 0.05;
    private const double BoundaryInsetEpsilon = 0.04;
    private static readonly double[] PlacementFloorRatios = { 0.80, 0.60, 0.40, 0.0 };

    public static IReadOnlyList<SectorGateLink> Generate(GeneratorConfig config, RegionCell region, IReadOnlyList<SectorCell> sectors, IReadOnlyList<OwnedPoint3> ownedSamples, Random random)
    {
        if (sectors.Count <= 1 || ownedSamples.Count == 0)
        {
            return Array.Empty<SectorGateLink>();
        }

        var mediumRange = config.MinimumStarDistanceLy * 6.0;
        var maximumPlacementRange = mediumRange * 0.5;
        var targetTotals = sectors.ToDictionary(sector => sector.Index, _ => MinimumLinksPerSector + random.Next(0, 2));
        var candidates = BuildCandidates(sectors, ownedSamples, maximumPlacementRange, random);
        var selected = new List<SectorGateLink>();
        var degrees = sectors.ToDictionary(sector => sector.Index, _ => 0);

        FillMandatoryLinks(sectors, candidates, selected, degrees);
        FillAdditionalLinks(sectors, candidates, selected, degrees, targetTotals);
        ValidateSelectedLinks(region, sectors, selected);

        return selected;
    }

    private static void FillMandatoryLinks(IReadOnlyList<SectorCell> sectors, IReadOnlyList<SectorGateCandidate> candidates, List<SectorGateLink> selected, Dictionary<int, int> degrees)
    {
        var progress = true;
        while (progress)
        {
            progress = false;
            foreach (var sector in sectors.OrderBy(sector => degrees[sector.Index]).ThenBy(sector => sector.Index))
            {
                if (degrees[sector.Index] >= MinimumLinksPerSector)
                {
                    continue;
                }

                var choice = candidates
                    .Where(candidate => candidate.SectorA == sector.Index || candidate.SectorB == sector.Index)
                    .Where(candidate => !ContainsLink(selected, candidate.SectorA, candidate.SectorB))
                    .OrderByDescending(candidate => ScoreCandidate(candidate, selected, sector.Index))
                    .FirstOrDefault();

                if (choice is null)
                {
                    continue;
                }

                AddLink(selected, degrees, choice);
                progress = true;
            }
        }
    }

    private static void FillAdditionalLinks(IReadOnlyList<SectorCell> sectors, IReadOnlyList<SectorGateCandidate> candidates, List<SectorGateLink> selected, Dictionary<int, int> degrees, Dictionary<int, int> targetTotals)
    {
        var progress = true;
        while (progress)
        {
            progress = false;
            foreach (var sector in sectors.OrderBy(sector => degrees[sector.Index]).ThenBy(sector => sector.Index))
            {
                if (degrees[sector.Index] >= targetTotals[sector.Index])
                {
                    continue;
                }

                var choice = candidates
                    .Where(candidate => candidate.SectorA == sector.Index || candidate.SectorB == sector.Index)
                    .Where(candidate => !ContainsLink(selected, candidate.SectorA, candidate.SectorB))
                    .Where(candidate => degrees[candidate.SectorA] < targetTotals[candidate.SectorA] && degrees[candidate.SectorB] < targetTotals[candidate.SectorB])
                    .OrderByDescending(candidate => ScoreCandidate(candidate, selected, sector.Index))
                    .FirstOrDefault();

                if (choice is null)
                {
                    continue;
                }

                AddLink(selected, degrees, choice);
                progress = true;
            }
        }
    }

    private static List<SectorGateCandidate> BuildCandidates(IReadOnlyList<SectorCell> sectors, IReadOnlyList<OwnedPoint3> ownedSamples, double maximumPlacementRange, Random random)
    {
        var evidence = CollectAdjacencyEvidence(sectors, ownedSamples);
        var sectorNuclei = sectors.OrderBy(sector => sector.Index).Select(sector => sector.Nucleus).ToList();
        var sectorSamples = BuildSectorSamplePools(sectors, ownedSamples, sectorNuclei);
        var candidates = new List<SectorGateCandidate>(evidence.Count);

        foreach (var item in evidence.Values)
        {
            if (item.LeftBoundary.Count == 0 || item.RightBoundary.Count == 0)
            {
                continue;
            }

            var leftSector = sectors[item.LeftIndex];
            var rightSector = sectors[item.RightIndex];
            var leftBoundary = Average(item.LeftBoundary);
            var rightBoundary = Average(item.RightBoundary);
            var corridorDirection = Normalize(Subtract(rightSector.Nucleus, leftSector.Nucleus));
            var boundaryGap = Distance(leftBoundary, rightBoundary);
            var leftCloud = sectorSamples.GetValueOrDefault(leftSector.Index) ?? new List<Point3> { leftSector.Nucleus };
            var rightCloud = sectorSamples.GetValueOrDefault(rightSector.Index) ?? new List<Point3> { rightSector.Nucleus };
            var candidate = TryBuildCandidate(leftSector, rightSector, leftBoundary, rightBoundary, corridorDirection, boundaryGap, leftCloud, rightCloud, sectorNuclei, maximumPlacementRange, random);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        return candidates;
    }

    private static Dictionary<int, List<Point3>> BuildSectorSamplePools(IReadOnlyList<SectorCell> sectors, IReadOnlyList<OwnedPoint3> ownedSamples, IReadOnlyList<Point3> sectorNuclei)
    {
        var rankedBySector = sectors.ToDictionary(
            sector => sector.Index,
            _ => new List<(Point3 Position, double WallDistance)>());

        foreach (var sample in ownedSamples)
        {
            var ownerDistance = Math.Sqrt(DistanceSquared(sample.Position, sectorNuclei[sample.OwnerIndex]));
            var secondNearestIndex = FindSecondNearestSectorIndex(sample.Position, sample.OwnerIndex, sectorNuclei);
            if (secondNearestIndex < 0)
            {
                rankedBySector[sample.OwnerIndex].Add((sample.Position, double.MaxValue));
                continue;
            }

            var secondDistance = Math.Sqrt(DistanceSquared(sample.Position, sectorNuclei[secondNearestIndex]));
            var wallDistance = Math.Max(0.0, (secondDistance - ownerDistance) * 0.5);
            rankedBySector[sample.OwnerIndex].Add((sample.Position, wallDistance));
        }

        var filtered = new Dictionary<int, List<Point3>>(rankedBySector.Count);
        foreach (var entry in rankedBySector)
        {
            var ranked = entry.Value.OrderBy(item => item.WallDistance).ToList();
            if (ranked.Count == 0)
            {
                filtered[entry.Key] = new List<Point3>();
                continue;
            }

            var cutoffIndex = Math.Min(ranked.Count - 1, Math.Max(0, (int)Math.Floor(ranked.Count * SectorWallMarginRatio)));
            var minimumWallDistance = ranked[cutoffIndex].WallDistance;
            var pool = ranked
                .Where(item => item.WallDistance >= minimumWallDistance)
                .OrderBy(item => item.WallDistance)
                .ThenBy(item => item.Position.X)
                .ThenBy(item => item.Position.Y)
                .ThenBy(item => item.Position.Z)
                .Select(item => item.Position)
                .ToList();

            if (pool.Count < Math.Min(12, ranked.Count))
            {
                pool = ranked
                    .OrderBy(item => item.WallDistance)
                    .ThenBy(item => item.Position.X)
                    .ThenBy(item => item.Position.Y)
                    .ThenBy(item => item.Position.Z)
                    .Select(item => item.Position)
                    .ToList();
            }

            filtered[entry.Key] = pool;
        }

        return filtered;
    }

    private static Dictionary<(int Left, int Right), SectorAdjacencyEvidence> CollectAdjacencyEvidence(IReadOnlyList<SectorCell> sectors, IReadOnlyList<OwnedPoint3> ownedSamples)
    {
        var nuclei = sectors.OrderBy(sector => sector.Index).Select(sector => sector.Nucleus).ToList();
        var evidence = new Dictionary<(int Left, int Right), SectorAdjacencyEvidence>();

        foreach (var sample in ownedSamples)
        {
            var nearestIndex = sample.OwnerIndex;
            var secondNearestIndex = FindSecondNearestSectorIndex(sample.Position, nearestIndex, nuclei);
            if (secondNearestIndex < 0)
            {
                continue;
            }

            var pair = nearestIndex < secondNearestIndex
                ? (nearestIndex, secondNearestIndex)
                : (secondNearestIndex, nearestIndex);

            if (!evidence.TryGetValue(pair, out var item))
            {
                item = new SectorAdjacencyEvidence(pair.Item1, pair.Item2);
                evidence.Add(pair, item);
            }

            var ownerDistance = DistanceSquared(sample.Position, nuclei[nearestIndex]);
            var neighborDistance = DistanceSquared(sample.Position, nuclei[secondNearestIndex]);
            var margin = Math.Abs(neighborDistance - ownerDistance);
            if (nearestIndex == item.LeftIndex)
            {
                item.AddLeft(sample.Position, margin, BoundaryEvidencePerSide);
            }
            else
            {
                item.AddRight(sample.Position, margin, BoundaryEvidencePerSide);
            }
        }

        return evidence;
    }

    private static SectorGateCandidate? TryBuildCandidate(
        SectorCell left,
        SectorCell right,
        Point3 leftBoundary,
        Point3 rightBoundary,
        Point3 corridorDirection,
        double boundaryGap,
        IReadOnlyList<Point3> leftCloud,
        IReadOnlyList<Point3> rightCloud,
        IReadOnlyList<Point3> sectorNuclei,
        double maximumPlacementRange,
        Random random)
    {
        SectorGateCandidate? bestCandidate = null;
        var (basisA, basisB) = BuildPerpendicularBasis(corridorDirection);

        foreach (var floorRatio in PlacementFloorRatios)
        {
            var minimumPreferredRange = maximumPlacementRange * floorRatio;

            for (var attempt = 0; attempt < 8; attempt++)
            {
                var leftTravelDirection = BuildPlacementDirection(Negate(corridorDirection), basisA, basisB, random, 0.34, 0.18);
                var rightTravelDirection = BuildPlacementDirection(corridorDirection, basisA, basisB, random, 0.34, 0.18);
                var inwardCapacityLeft = FindInwardCapacity(leftBoundary, leftTravelDirection, leftCloud);
                var inwardCapacityRight = FindInwardCapacity(rightBoundary, rightTravelDirection, rightCloud);
                var maxReachableDistance = Math.Min(maximumPlacementRange, boundaryGap + inwardCapacityLeft + inwardCapacityRight);
                if (maxReachableDistance < minimumPreferredRange)
                {
                    continue;
                }

                var distanceBand = maxReachableDistance - minimumPreferredRange;
                var targetDistance = maxReachableDistance - (distanceBand * (random.NextDouble() * 0.40));
                var requiredInset = Math.Max(0.0, targetDistance - boundaryGap);
                var split = 0.28 + (random.NextDouble() * 0.44);
                var leftInset = Math.Min(inwardCapacityLeft, requiredInset * split);
                var rightInset = Math.Min(inwardCapacityRight, requiredInset - leftInset);
                var remainingInset = requiredInset - leftInset - rightInset;

                if (remainingInset > 0.001)
                {
                    var leftExtra = Math.Min(Math.Max(0.0, inwardCapacityLeft - leftInset), remainingInset);
                    leftInset += leftExtra;
                    remainingInset -= leftExtra;
                }

                if (remainingInset > 0.001)
                {
                    var rightExtra = Math.Min(Math.Max(0.0, inwardCapacityRight - rightInset), remainingInset);
                    rightInset += rightExtra;
                    remainingInset -= rightExtra;
                }

                if (remainingInset > 0.20)
                {
                    continue;
                }

                var systemA = Add(leftBoundary, Multiply(leftTravelDirection, leftInset + BoundaryInsetEpsilon));
                var systemB = Add(rightBoundary, Multiply(rightTravelDirection, rightInset + BoundaryInsetEpsilon));
                if (!TrySnapToOwnedSamples(
                    leftCloud,
                    rightCloud,
                    leftBoundary,
                    rightBoundary,
                    leftTravelDirection,
                    rightTravelDirection,
                    systemA,
                    systemB,
                    maximumPlacementRange,
                    out systemA,
                    out systemB))
                {
                    continue;
                }

                var systemDistance = Distance(systemA, systemB);
                if (systemDistance < minimumPreferredRange || systemDistance > maximumPlacementRange)
                {
                    continue;
                }

                if (!IsOwnedBySector(systemA, left.Index, sectorNuclei) || !IsOwnedBySector(systemB, right.Index, sectorNuclei))
                {
                    continue;
                }

                var projectionAgreement =
                    ProjectionAgreementCount(systemA, left.Index, sectorNuclei) +
                    ProjectionAgreementCount(systemB, right.Index, sectorNuclei);

                var asymmetry = Math.Abs(leftInset - rightInset);
                var angularSpread = AngleDegrees(Negate(leftTravelDirection), rightTravelDirection);
                var weight =
                    (systemDistance * 2.8) -
                    (boundaryGap * 0.55) +
                    (asymmetry * 0.50) +
                    (angularSpread * 0.10) +
                    (projectionAgreement * 1.35) +
                    ((1.0 - floorRatio) * 8.0) +
                    (random.NextDouble() * 0.2);

                var candidate = new SectorGateCandidate(left.Index, right.Index, systemA, systemB, systemDistance, weight);
                if (bestCandidate is null || candidate.SelectionWeight > bestCandidate.SelectionWeight)
                {
                    bestCandidate = candidate;
                }
            }
        }

        return bestCandidate;
    }

    private static bool TrySnapToOwnedSamples(
        IReadOnlyList<Point3> leftCloud,
        IReadOnlyList<Point3> rightCloud,
        Point3 leftBoundary,
        Point3 rightBoundary,
        Point3 leftTravelDirection,
        Point3 rightTravelDirection,
        Point3 idealSystemA,
        Point3 idealSystemB,
        double maximumRange,
        out Point3 snappedSystemA,
        out Point3 snappedSystemB)
    {
        var leftAnchors = SelectAnchorSamples(leftCloud, leftBoundary, leftTravelDirection, idealSystemA);
        var rightAnchors = SelectAnchorSamples(rightCloud, rightBoundary, rightTravelDirection, idealSystemB);

        Point3? bestLeft = null;
        Point3? bestRight = null;
        var bestScore = double.MaxValue;

        foreach (var left in leftAnchors)
        {
            foreach (var right in rightAnchors)
            {
                var distance = Distance(left, right);
                if (distance > maximumRange)
                {
                    continue;
                }

                var score = DistanceSquared(left, idealSystemA) + DistanceSquared(right, idealSystemB) + Math.Abs(distance - Distance(idealSystemA, idealSystemB));
                if (score < bestScore)
                {
                    bestScore = score;
                    bestLeft = left;
                    bestRight = right;
                }
            }
        }

        if (bestLeft is null || bestRight is null)
        {
            snappedSystemA = default;
            snappedSystemB = default;
            return false;
        }

        snappedSystemA = bestLeft.Value;
        snappedSystemB = bestRight.Value;
        return true;
    }

    private static List<Point3> SelectAnchorSamples(IReadOnlyList<Point3> cloud, Point3 boundary, Point3 travelDirection, Point3 idealPoint)
    {
        var inwardProjections = cloud
            .Select(point => Dot(Subtract(point, boundary), travelDirection))
            .Where(projection => projection >= -0.001)
            .ToList();
        var maxProjection = inwardProjections.Count == 0 ? 0.0 : inwardProjections.Max();
        var preferredDepth = Math.Min(Math.Max(0.75, maxProjection * 0.22), 3.0);

        return cloud
            .Select(point => new
            {
                Point = point,
                InwardProjection = Dot(Subtract(point, boundary), travelDirection),
                IdealDistance = DistanceSquared(point, idealPoint),
                DepthPenalty = Math.Abs(Dot(Subtract(point, boundary), travelDirection) - preferredDepth)
            })
            .Where(item => item.InwardProjection >= -0.001)
            .OrderBy(item => item.DepthPenalty)
            .ThenBy(item => item.IdealDistance)
            .ThenBy(item => item.InwardProjection)
            .Take(AnchorCandidatesPerSide)
            .Select(item => item.Point)
            .ToList();
    }

    private static void ValidateSelectedLinks(RegionCell region, IReadOnlyList<SectorCell> sectors, IReadOnlyList<SectorGateLink> selected)
    {
        var sectorNuclei = sectors.OrderBy(sector => sector.Index).Select(sector => sector.Nucleus).ToList();
        foreach (var link in selected)
        {
            if (!IsOwnedBySector(link.SystemA, link.SectorA, sectorNuclei) || !IsOwnedBySector(link.SystemB, link.SectorB, sectorNuclei))
            {
                throw new InvalidOperationException($"Sector gate endpoint validation failed in {region.Name} for sectors {link.SectorA} and {link.SectorB}.");
            }

        }
    }

    private static bool IsOwnedBySector(Point3 point, int expectedSectorIndex, IReadOnlyList<Point3> sectorNuclei)
    {
        return FindNearestSectorIndex(point, sectorNuclei) == expectedSectorIndex;
    }

    private static bool IsRenderableInsideSector(Point3 point, int expectedSectorIndex, IReadOnlyList<Point3> sectorNuclei)
    {
        return
            FindNearestProjectedSectorIndex(ProjectTopDown(point), sectorNuclei, ProjectTopDown) == expectedSectorIndex &&
            FindNearestProjectedSectorIndex(ProjectSide(point), sectorNuclei, ProjectSide) == expectedSectorIndex &&
            FindNearestProjectedSectorIndex(ProjectAngled(point), sectorNuclei, ProjectAngled) == expectedSectorIndex;
    }

    private static int ProjectionAgreementCount(Point3 point, int expectedSectorIndex, IReadOnlyList<Point3> sectorNuclei)
    {
        var agreements = 0;
        if (FindNearestProjectedSectorIndex(ProjectTopDown(point), sectorNuclei, ProjectTopDown) == expectedSectorIndex)
        {
            agreements++;
        }

        if (FindNearestProjectedSectorIndex(ProjectSide(point), sectorNuclei, ProjectSide) == expectedSectorIndex)
        {
            agreements++;
        }

        if (FindNearestProjectedSectorIndex(ProjectAngled(point), sectorNuclei, ProjectAngled) == expectedSectorIndex)
        {
            agreements++;
        }

        return agreements;
    }

    private static int FindNearestProjectedSectorIndex(Point2 projectedPoint, IReadOnlyList<Point3> nuclei, Func<Point3, Point2> projector)
    {
        var nearestIndex = 0;
        var nearestDistanceSquared = double.MaxValue;

        for (var index = 0; index < nuclei.Count; index++)
        {
            var projectedNucleus = projector(nuclei[index]);
            var dx = projectedPoint.X - projectedNucleus.X;
            var dy = projectedPoint.Y - projectedNucleus.Y;
            var distanceSquared = (dx * dx) + (dy * dy);
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestIndex = index;
            }
        }

        return nearestIndex;
    }

    private static Point2 ProjectTopDown(Point3 point) => new(point.X, -point.Y);
    private static Point2 ProjectSide(Point3 point) => new(point.X, -point.Z);

    private static Point2 ProjectAngled(Point3 point)
    {
        var x = (point.X - point.Y) * 0.8660254038;
        var y = ((point.X + point.Y) * 0.5) - (point.Z * 1.2);
        return new Point2(x, -y);
    }

    private static int FindNearestSectorIndex(Point3 sample, IReadOnlyList<Point3> nuclei)
    {
        var nearestIndex = 0;
        var nearestDistanceSquared = double.MaxValue;

        for (var index = 0; index < nuclei.Count; index++)
        {
            var distanceSquared = DistanceSquared(sample, nuclei[index]);
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestIndex = index;
            }
        }

        return nearestIndex;
    }

    private static int FindSecondNearestSectorIndex(Point3 sample, int excludedIndex, IReadOnlyList<Point3> nuclei)
    {
        var nearestIndex = -1;
        var nearestDistanceSquared = double.MaxValue;

        for (var index = 0; index < nuclei.Count; index++)
        {
            if (index == excludedIndex)
            {
                continue;
            }

            var distanceSquared = DistanceSquared(sample, nuclei[index]);
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestIndex = index;
            }
        }

        return nearestIndex;
    }

    private static double ScoreCandidate(SectorGateCandidate candidate, IReadOnlyList<SectorGateLink> selected, int focusSector)
    {
        var direction = candidate.SectorA == focusSector
            ? Normalize(Subtract(candidate.SystemB, candidate.SystemA))
            : Normalize(Subtract(candidate.SystemA, candidate.SystemB));

        var existingDirections = selected
            .Where(link => link.SectorA == focusSector || link.SectorB == focusSector)
            .Select(link => link.SectorA == focusSector
                ? Normalize(Subtract(link.SystemB, link.SystemA))
                : Normalize(Subtract(link.SystemA, link.SystemB)))
            .ToList();

        var spacingScore = existingDirections.Count == 0
            ? 180.0
            : existingDirections.Min(existing => AngleDegrees(existing, direction));

        return candidate.SelectionWeight + spacingScore;
    }

    private static void AddLink(List<SectorGateLink> selected, Dictionary<int, int> degrees, SectorGateCandidate candidate)
    {
        selected.Add(candidate.ToLink());
        degrees[candidate.SectorA]++;
        degrees[candidate.SectorB]++;
    }

    private static bool ContainsLink(IReadOnlyList<SectorGateLink> selected, int sectorA, int sectorB)
    {
        return selected.Any(link =>
            (link.SectorA == sectorA && link.SectorB == sectorB) ||
            (link.SectorA == sectorB && link.SectorB == sectorA));
    }

    private static double FindInwardCapacity(Point3 boundary, Point3 direction, IReadOnlyList<Point3> cloud)
    {
        var best = 0.0;
        foreach (var point in cloud)
        {
            var projection = Dot(Subtract(point, boundary), direction);
            if (projection > best)
            {
                best = projection;
            }
        }

        return best;
    }

    private static Point3 Average(IReadOnlyList<Point3> points)
    {
        var sumX = 0.0;
        var sumY = 0.0;
        var sumZ = 0.0;
        foreach (var point in points)
        {
            sumX += point.X;
            sumY += point.Y;
            sumZ += point.Z;
        }

        var count = Math.Max(1, points.Count);
        return new Point3(sumX / count, sumY / count, sumZ / count);
    }

    private static Point3 BuildPlacementDirection(Point3 inwardDirection, Point3 basisA, Point3 basisB, Random random, double maxBasisAWeight, double maxBasisBWeight)
    {
        var blendA = (random.NextDouble() * 2.0 - 1.0) * maxBasisAWeight;
        var blendB = (random.NextDouble() * 2.0 - 1.0) * maxBasisBWeight;
        return Normalize(Add(inwardDirection, Add(Multiply(basisA, blendA), Multiply(basisB, blendB))));
    }

    private static (Point3 BasisA, Point3 BasisB) BuildPerpendicularBasis(Point3 direction)
    {
        var reference = Math.Abs(direction.Z) < 0.82
            ? new Point3(0, 0, 1)
            : new Point3(0, 1, 0);
        var basisA = Normalize(Cross(direction, reference));
        var basisB = Normalize(Cross(direction, basisA));
        return (basisA, basisB);
    }

    private static double AngleDegrees(Point3 left, Point3 right)
    {
        var dot = Math.Clamp(Dot(left, right), -1.0, 1.0);
        return Math.Acos(dot) * (180.0 / Math.PI);
    }

    private static double DistanceSquared(Point3 left, Point3 right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        var dz = left.Z - right.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    private static double Distance(Point3 left, Point3 right) => Math.Sqrt(DistanceSquared(left, right));
    private static Point3 Add(Point3 left, Point3 right) => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    private static Point3 Subtract(Point3 left, Point3 right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    private static Point3 Multiply(Point3 point, double scalar) => new(point.X * scalar, point.Y * scalar, point.Z * scalar);
    private static Point3 Negate(Point3 point) => new(-point.X, -point.Y, -point.Z);
    private static Point3 Cross(Point3 left, Point3 right) => new(
        (left.Y * right.Z) - (left.Z * right.Y),
        (left.Z * right.X) - (left.X * right.Z),
        (left.X * right.Y) - (left.Y * right.X));
    private static double Dot(Point3 left, Point3 right) => (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    private static Point3 Normalize(Point3 point)
    {
        var length = Math.Sqrt((point.X * point.X) + (point.Y * point.Y) + (point.Z * point.Z));
        if (length <= 0.0)
        {
            return new Point3(1, 0, 0);
        }

        return new Point3(point.X / length, point.Y / length, point.Z / length);
    }

    private sealed class SectorAdjacencyEvidence
    {
        private readonly List<(Point3 Position, double Margin)> _left = new();
        private readonly List<(Point3 Position, double Margin)> _right = new();

        public SectorAdjacencyEvidence(int leftIndex, int rightIndex)
        {
            LeftIndex = leftIndex;
            RightIndex = rightIndex;
        }

        public int LeftIndex { get; }
        public int RightIndex { get; }
        public IReadOnlyList<Point3> LeftBoundary => _left.OrderBy(item => item.Margin).Select(item => item.Position).ToList();
        public IReadOnlyList<Point3> RightBoundary => _right.OrderBy(item => item.Margin).Select(item => item.Position).ToList();

        public void AddLeft(Point3 position, double margin, int limit) => Add(_left, position, margin, limit);
        public void AddRight(Point3 position, double margin, int limit) => Add(_right, position, margin, limit);

        private static void Add(List<(Point3 Position, double Margin)> items, Point3 position, double margin, int limit)
        {
            items.Add((position, margin));
            if (items.Count > limit)
            {
                var worstIndex = 0;
                var worstMargin = items[0].Margin;
                for (var index = 1; index < items.Count; index++)
                {
                    if (items[index].Margin > worstMargin)
                    {
                        worstMargin = items[index].Margin;
                        worstIndex = index;
                    }
                }

                items.RemoveAt(worstIndex);
            }
        }
    }

    private sealed record SectorGateCandidate(int SectorA, int SectorB, Point3 SystemA, Point3 SystemB, double DistanceLy, double SelectionWeight)
    {
        public SectorGateLink ToLink() => new(SectorA, SectorB, SystemA, SystemB, DistanceLy);
    }
}

static class RegionPalette
{
    private static readonly string[] Colors =
    {
        "#ff6b6b", "#ffd166", "#06d6a0", "#4cc9f0",
        "#f72585", "#b8de6f", "#f4a261", "#90be6d",
        "#577590", "#c77dff", "#43aa8b", "#e76f51",
        "#72efdd", "#ff9f1c", "#a8dadc", "#9b5de5"
    };

    public static string GetColor(int index) => Colors[index % Colors.Length];

    public static string GetSectorColor(int regionIndex, int sectorIndex)
    {
        var primary = Colors[(regionIndex + sectorIndex) % Colors.Length];
        var accent = Colors[((regionIndex * 5) + sectorIndex + 3) % Colors.Length];
        var mix = 0.24 + ((sectorIndex % 4) * 0.10);
        return BlendColors(primary, accent, mix);
    }

    private static string BlendColors(string leftHex, string rightHex, double mix)
    {
        var left = leftHex.TrimStart('#');
        var right = rightHex.TrimStart('#');
        var red = ClampColor((Convert.ToInt32(left.Substring(0, 2), 16) * (1.0 - mix)) + (Convert.ToInt32(right.Substring(0, 2), 16) * mix));
        var green = ClampColor((Convert.ToInt32(left.Substring(2, 2), 16) * (1.0 - mix)) + (Convert.ToInt32(right.Substring(2, 2), 16) * mix));
        var blue = ClampColor((Convert.ToInt32(left.Substring(4, 2), 16) * (1.0 - mix)) + (Convert.ToInt32(right.Substring(4, 2), 16) * mix));
        return $"#{red:X2}{green:X2}{blue:X2}";
    }

    private static int ClampColor(double value) => (int)Math.Clamp(Math.Round(value), 0.0, 255.0);
}

static class StableSeedHasher
{
    public static int HashToInt32(string text)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var character in text)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return (int)hash;
        }
    }
}

readonly record struct Point2(double X, double Y);
readonly record struct Point3(double X, double Y, double Z);
readonly record struct OwnedPoint3(Point3 Position, int OwnerIndex);
readonly record struct Span3(double X, double Y, double Z);
readonly record struct RegionCell(int Index, string Name, Point3 Nucleus, string ColorHex);
readonly record struct SectorCell(int Index, string Name, Point3 Nucleus, string ColorHex);
readonly record struct SectorGateLink(int SectorA, int SectorB, Point3 SystemA, Point3 SystemB, double DistanceLy);
readonly record struct RegionSectorSet(RegionCell Region, IReadOnlyList<SectorCell> Sectors, IReadOnlyList<OwnedPoint3> OwnedSamples, IReadOnlyList<SectorGateLink> GateLinks);
readonly record struct MeshQuad(int A, int B, int C, int D);
readonly record struct RegionSurfaceMesh(int RegionIndex, string Name, string ColorHex, IReadOnlyList<Point3> Vertices, IReadOnlyList<MeshQuad> Quads);
readonly record struct HeavyGateLink(int RegionA, int RegionB, Point3 SystemA, Point3 SystemB, double DistanceLy, bool IsAdjacent);
readonly record struct TerritoryRegionStructureData(Span3 Span, IReadOnlyList<RegionCell> Regions, IReadOnlyList<RegionSectorSet> RegionSectors, IReadOnlyList<RegionSurfaceMesh> SurfaceMeshes, IReadOnlyList<HeavyGateLink> HeavyGateLinks);
readonly record struct NetworkGate(int TargetSystemId, string GateType, double DistanceLy, string TargetScope);
readonly record struct NetworkSystemNode(int Id, int RegionIndex, int SectorIndex, Point3 Position, string Name, string Address, string SourceType, string SourceDescription, IReadOnlyList<NetworkGate> Gates);
readonly record struct NetworkLinkEdge(int SystemAId, int SystemBId, string GateType, double DistanceLy, string Scope);
sealed record TerritoryNetworkSnapshot(IReadOnlyList<NetworkSystemNode> Systems, IReadOnlyList<NetworkLinkEdge> Links);

readonly record struct ProjectionBounds(double MinX, double MaxX, double MinY, double MaxY)
{
    public static ProjectionBounds From(IEnumerable<Point2> points)
    {
        var minX = double.MaxValue;
        var maxX = double.MinValue;
        var minY = double.MaxValue;
        var maxY = double.MinValue;

        foreach (var point in points)
        {
            minX = Math.Min(minX, point.X);
            maxX = Math.Max(maxX, point.X);
            minY = Math.Min(minY, point.Y);
            maxY = Math.Max(maxY, point.Y);
        }

        return new ProjectionBounds(minX, maxX, minY, maxY);
    }
}

sealed record SolarSystemReport(string Html, int SystemCount, string PathReportFileName, string PathReportHtml);
sealed record GenerationResult(string OutputDirectory, string DiagramPath, string ShadedDiagramPath, IReadOnlyList<string> SectorDiagramPaths, string HeavyLinkDiagramPath, string RegionLinkReportPath, string SolarSystemReportPath, string PathValidationReportPath, string InteractiveViewerPath, int RegionCount, Span3 TerritorySpan, int HeavyLinkCount, int SolarSystemCount, string Status, string Message);