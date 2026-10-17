using System.Windows;
using System.Windows.Markup;

namespace AIUsageWidget;

public static class WidgetTheme
{
    public static void Apply(Window window)
    {
        var resources = (ResourceDictionary)XamlReader.Parse("""
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Style TargetType="TabItem">
    <Setter Property="Background" Value="#252E3A"/><Setter Property="Foreground" Value="#ABB9CC"/>
    <Setter Property="Padding" Value="14,9"/><Setter Property="Margin" Value="0,0,6,0"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabItem">
      <Border Background="{TemplateBinding Background}" CornerRadius="7" Padding="{TemplateBinding Padding}">
        <ContentPresenter ContentSource="Header" HorizontalAlignment="Center" VerticalAlignment="Center"/>
      </Border>
    </ControlTemplate></Setter.Value></Setter>
    <Style.Triggers><Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="#3A4D68"/><Setter Property="Foreground" Value="#F3F6FC"/></Trigger>
      <Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#354256"/></Trigger>
    </Style.Triggers>
  </Style>
  <Style TargetType="ComboBoxItem">
    <Setter Property="Background" Value="#252E3A"/><Setter Property="Foreground" Value="#EDF2F8"/><Setter Property="Padding" Value="10,7"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem"><Border Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border></ControlTemplate></Setter.Value></Setter>
    <Style.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter Property="Background" Value="#3A4D68"/></Trigger></Style.Triggers>
  </Style>
  <Style TargetType="ComboBox">
    <Setter Property="Foreground" Value="#EDF2F8"/><Setter Property="Background" Value="#252E3A"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
      <Grid>
        <ToggleButton Focusable="False" ClickMode="Press" IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
          <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="#252E3A" BorderBrush="#405069" BorderThickness="1" CornerRadius="7" Padding="10,7"><Grid><TextBlock Text="⌄" Foreground="#ABB9CC" HorizontalAlignment="Right"/></Grid></Border></ControlTemplate></ToggleButton.Template>
        </ToggleButton>
        <ContentPresenter Content="{TemplateBinding SelectionBoxItem}" Margin="10,7,28,7" IsHitTestVisible="False" VerticalAlignment="Center"/>
        <Popup x:Name="PART_Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False" PopupAnimation="Fade">
          <Border Background="#252E3A" BorderBrush="#405069" BorderThickness="1" CornerRadius="7" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}" Padding="3"><ScrollViewer MaxHeight="280"><ItemsPresenter/></ScrollViewer></Border>
        </Popup>
      </Grid>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType="ScrollBar">
    <Setter Property="Width" Value="11"/><Setter Property="Background" Value="Transparent"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
      <Grid Background="Transparent"><Track x:Name="PART_Track" IsDirectionReversed="True" Orientation="{TemplateBinding Orientation}" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" ViewportSize="{TemplateBinding ViewportSize}" Value="{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
        <Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0" Focusable="False"/></Track.DecreaseRepeatButton>
        <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="#4B596D" CornerRadius="4" Margin="2"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
        <Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0" Focusable="False"/></Track.IncreaseRepeatButton>
      </Track></Grid>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
</ResourceDictionary>
""");
        window.Resources.MergedDictionaries.Add(resources);
    }
}
