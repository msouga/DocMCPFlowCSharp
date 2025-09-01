public class DiagramPlanItem
{
    public string Name { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty; // plantuml | mermaid | texto
    public string Placement { get; set; } = "end";    // start | end | before_para:N | after_para:N
    public string Code { get; set; } = string.Empty;   // opcional, código del diagrama si aplica
}

