using Npgsql;
using RefaccionariaPOS.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace RefaccionariaPOS.Views
{
    public class RegistroAuditoria
    {
        public int Id { get; set; }
        public string Usuario { get; set; } = "Sistema";
        public string Tabla { get; set; } = string.Empty;
        public string Operacion { get; set; } = string.Empty;
        public int RegistroId { get; set; }
        public string Campo { get; set; } = string.Empty;
        public string ValorAnterior { get; set; } = string.Empty;
        public string ValorNuevo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public DateTime FechaOperacion { get; set; }
    }

    public partial class AuditoriaView : Window
    {
        private const string QueryAuditoria = @"
            SELECT a.id, COALESCE(u.username, 'Sistema') AS usuario, a.tabla, a.operacion,
                   a.registro_id, COALESCE(a.campo, '') AS campo, COALESCE(a.valor_anterior, '') AS valor_anterior,
                   COALESCE(a.valor_nuevo, '') AS valor_nuevo, a.descripcion, a.fecha_operacion
            FROM auditoria a
            LEFT JOIN usuarios u ON u.id = a.usuario_id
            WHERE (@tabla = '' OR a.tabla = @tabla)
              AND (@operacion = '' OR a.operacion = @operacion)
              AND (@diasAtras = 0 OR a.fecha_operacion >= CURRENT_TIMESTAMP - (@diasAtras || ' days')::INTERVAL)
            ORDER BY a.fecha_operacion DESC
            LIMIT 500;";

        private const string QueryHistorialInventario = @"
            SELECT h.id, COALESCE(u.username, 'Sistema') AS usuario, p.nombre AS descripcion,
                   h.tipo AS operacion, h.producto_id AS registro_id, h.stock_anterior AS valor_anterior,
                   h.stock_nuevo AS valor_nuevo, h.cantidad, h.razon, h.fecha_movimiento
            FROM historial_inventario h
            LEFT JOIN usuarios u ON u.id = h.usuario_registrador_id
            LEFT JOIN productos p ON p.id = h.producto_id
            WHERE (@tipo = '' OR h.tipo = @tipo)
              AND (@diasAtras = 0 OR h.fecha_movimiento >= CURRENT_TIMESTAMP - (@diasAtras || ' days')::INTERVAL)
            ORDER BY h.fecha_movimiento DESC
            LIMIT 500;";

        private ObservableCollection<RegistroAuditoria> registros = new();
        private string tablaSeleccionada = "";
        private string operacionSeleccionada = "";
        private int diasAtras = 30;

        public AuditoriaView()
        {
            InitializeComponent();
            dgAuditoria.ItemsSource = registros;
            CargarFiltros();
            CargarAuditoria();
        }

        private void CargarFiltros()
        {
            var tablas = new List<string> { "", "productos", "ventas", "usuarios", "historial_inventario" };
            var operaciones = new List<string> { "", "INSERT", "UPDATE", "DELETE", "Venta", "Agregación", "Ajuste Manual" };

            cmbTabla.ItemsSource = tablas;
            cmbOperacion.ItemsSource = operaciones;
            cmbTabla.SelectedIndex = 0;
            cmbOperacion.SelectedIndex = 0;

            // Cargar días
            var dias = new List<string> { "Hoy (1 día)", "Últimos 7 días", "Últimos 30 días", "Últimos 90 días", "Todo" };
            cmbDias.ItemsSource = dias;
            cmbDias.SelectedIndex = 2; // 30 días por defecto
        }

        private async void CargarAuditoria()
        {
            try
            {
                registros.Clear();
                List<RegistroAuditoria> lista = await ObtenerRegistrosAsync();
                foreach (var registro in lista)
                {
                    registros.Add(registro);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar auditoría: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async System.Threading.Tasks.Task<List<RegistroAuditoria>> ObtenerRegistrosAsync()
        {
            List<RegistroAuditoria> resultado = new();
            DatabaseConnection db = new DatabaseConnection();

            using (NpgsqlConnection conexion = db.GetConnection())
            {
                await conexion.OpenAsync();

                using (NpgsqlCommand cmd = new NpgsqlCommand(QueryAuditoria, conexion))
                {
                    cmd.Parameters.AddWithValue("@tabla", tablaSeleccionada ?? "");
                    cmd.Parameters.AddWithValue("@operacion", operacionSeleccionada ?? "");
                    cmd.Parameters.AddWithValue("@diasAtras", diasAtras);

                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            resultado.Add(new RegistroAuditoria
                            {
                                Id = Convert.ToInt32(reader["id"]),
                                Usuario = reader["usuario"].ToString() ?? "Sistema",
                                Tabla = reader["tabla"].ToString() ?? string.Empty,
                                Operacion = reader["operacion"].ToString() ?? string.Empty,
                                RegistroId = Convert.ToInt32(reader["registro_id"]),
                                Campo = reader["campo"].ToString() ?? string.Empty,
                                ValorAnterior = reader["valor_anterior"].ToString() ?? string.Empty,
                                ValorNuevo = reader["valor_nuevo"].ToString() ?? string.Empty,
                                Descripcion = reader["descripcion"].ToString() ?? string.Empty,
                                FechaOperacion = Convert.ToDateTime(reader["fecha_operacion"])
                            });
                        }
                    }
                }
            }

            return resultado;
        }

        private void CmbTabla_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            tablaSeleccionada = cmbTabla.SelectedItem?.ToString() ?? "";
            CargarAuditoria();
        }

        private void CmbOperacion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            operacionSeleccionada = cmbOperacion.SelectedItem?.ToString() ?? "";
            CargarAuditoria();
        }

        private void CmbDias_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            diasAtras = cmbDias.SelectedIndex switch
            {
                0 => 1,
                1 => 7,
                2 => 30,
                3 => 90,
                _ => 0
            };
            CargarAuditoria();
        }

        private void BtnExportar_Click(object sender, RoutedEventArgs e)
        {
            if (registros.Count == 0)
            {
                MessageBox.Show("No hay registros para exportar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var saveDialog = new System.Windows.Forms.SaveFileDialog
            {
                Filter = "CSV|*.csv",
                FileName = $"auditoria_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv"
            };

            if (saveDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ExportarACSV(saveDialog.FileName);
            }
        }

        private void ExportarACSV(string filePath)
        {
            try
            {
                using (var writer = new System.IO.StreamWriter(filePath))
                {
                    writer.WriteLine("ID,Usuario,Tabla,Operación,ID Registro,Campo,Valor Anterior,Valor Nuevo,Descripción,Fecha");

                    foreach (var registro in registros)
                    {
                        writer.WriteLine($"\"{registro.Id}\",\"{registro.Usuario}\",\"{registro.Tabla}\"," +
                            $"\"{registro.Operacion}\",\"{registro.RegistroId}\",\"{registro.Campo}\"," +
                            $"\"{registro.ValorAnterior}\",\"{registro.ValorNuevo}\",\"{registro.Descripcion}\"," +
                            $"\"{registro.FechaOperacion:yyyy-MM-dd HH:mm:ss}\"");
                    }
                }

                MessageBox.Show($"Auditoría exportada a: {filePath}", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al exportar: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
