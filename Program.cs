using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FileStorage
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateWebHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args)
        {
            return WebHost.CreateDefaultBuilder(args)
                .UseUrls("http://localhost:5000")
                .ConfigureServices(services =>
                {
                    services.AddMvc();
                    services.AddLogging(logging =>
                    {
                        logging.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.None);
                        logging.AddConsole();
                    });
                })
                .Configure(app =>
                {
                    app.UseDeveloperExceptionPage();
                    app.UseMvc();

                    var logger = app.ApplicationServices.GetService<ILogger<Program>>();
                    string projectRoot = Directory.GetCurrentDirectory();
                    while (Path.GetFileName(projectRoot) == "Debug" || Path.GetFileName(projectRoot) == "bin")
                    {
                        projectRoot = Directory.GetParent(projectRoot).FullName;
                    }
                    string storagePath = Path.Combine(projectRoot, "Storage");
                    if (!Directory.Exists(storagePath))
                    {
                        Directory.CreateDirectory(storagePath);
                        logger.LogInformation("Создана папка для хранения файлов: {StoragePath}", storagePath);
                    }
                    logger.LogInformation("Сервер запущен на http://localhost:5000");
                })
                .UseEnvironment("Development");
        }
    }

    [Route("/")]
    [ApiController]
    public class FileStorageController : ControllerBase
    {
        private readonly string _storagePath;
        private readonly ILogger _logger;

        public FileStorageController(ILogger<FileStorageController> logger)
        {
            string projectRoot = Directory.GetCurrentDirectory();
            while (Path.GetFileName(projectRoot) == "Debug" || Path.GetFileName(projectRoot) == "bin")
            {
                projectRoot = Directory.GetParent(projectRoot).FullName;
            }
            _storagePath = Path.Combine(projectRoot, "Storage");
            _logger = logger;
        }

        // PUT: Загрузка файла
        [HttpPut("{*path}")]
        public async Task<IActionResult> UploadFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                _logger.LogWarning("Попытка загрузки файла без указания пути");
                return BadRequest("Путь обязателен.");
            }

            string fullPath = Path.Combine(_storagePath, path.Replace("/", Path.DirectorySeparatorChar.ToString()));
            string directory = Path.GetDirectoryName(fullPath);

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogInformation("Создана папка: {Directory}", directory);
            }

            try
            {
                using (var fileStream = new FileStream(fullPath, FileMode.Create))
                {
                    await Request.Body.CopyToAsync(fileStream);
                }
                _logger.LogInformation("Файл загружен: {FilePath}", fullPath);
                return Ok("Файл успешно загружен.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при загрузке файла: {FilePath}", fullPath);
                return StatusCode(500, "Ошибка при загрузке файла: " + ex.Message);
            }
        }

        // GET: Скачивание файла или список папки
        [HttpGet("{*path}")]
        public IActionResult GetFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                _logger.LogInformation("Запрошен список файлов в корневой папке");
                return ListDirectory("");
            }

            string fullPath = Path.Combine(_storagePath, path.Replace("/", Path.DirectorySeparatorChar.ToString()));

            if (Directory.Exists(fullPath))
            {
                _logger.LogInformation("Запрошен список файлов в папке: {Directory}", fullPath);
                return ListDirectory(path);
            }

            if (!System.IO.File.Exists(fullPath))
            {
                _logger.LogWarning("Файл не найден: {FilePath}", fullPath);
                return NotFound("Файл не найден.");
            }

            try
            {
                var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
                string contentType = "application/octet-stream";
                _logger.LogInformation("Файл отправлен: {FilePath}", fullPath);
                return File(fileStream, contentType, Path.GetFileName(fullPath));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при скачивании файла: {FilePath}", fullPath);
                return StatusCode(500, "Ошибка при скачивании файла: " + ex.Message);
            }
        }

        // GET: Список содержимого папки
        private IActionResult ListDirectory(string path)
        {
            string fullPath = Path.Combine(_storagePath, path.Replace("/", Path.DirectorySeparatorChar.ToString()));
            if (!Directory.Exists(fullPath))
            {
                _logger.LogWarning("Папка не найдена: {Directory}", fullPath);
                return NotFound("Папка не найдена.");
            }

            try
            {
                var files = Directory.GetFiles(fullPath).Select(f => new
                {
                    Name = Path.GetFileName(f),
                    Type = "file"
                });

                var directories = Directory.GetDirectories(fullPath).Select(d => new
                {
                    Name = Path.GetFileName(d),
                    Type = "directory"
                });

                var result = files.Concat(directories).OrderBy(item => item.Type).ThenBy(item => item.Name).ToList();
                _logger.LogInformation("Возвращён список содержимого папки: {Directory}", fullPath);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении списка папки: {Directory}", fullPath);
                return StatusCode(500, "Ошибка при получении списка папки: " + ex.Message);
            }
        }

        // HEAD: Получение метаданных файла
        [HttpHead("{*path}")]
        public IActionResult GetFileMetadata(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                _logger.LogWarning("Попытка получения метаданных без указания пути");
                return BadRequest("Путь обязателен.");
            }

            string fullPath = Path.Combine(_storagePath, path.Replace("/", Path.DirectorySeparatorChar.ToString()));

            if (!System.IO.File.Exists(fullPath))
            {
                _logger.LogWarning("Файл не найден для метаданных: {FilePath}", fullPath);
                return NotFound("Файл не найден.");
            }

            try
            {
                var fileInfo = new FileInfo(fullPath);
                Response.Headers.Add("Content-Length", fileInfo.Length.ToString());
                Response.Headers.Add("Last-Modified", fileInfo.LastWriteTimeUtc.ToString("R"));
                _logger.LogInformation("Метаданные файла отправлены: {FilePath}", fullPath);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении метаданных: {FilePath}", fullPath);
                return StatusCode(500, "Ошибка при получении метаданных: " + ex.Message);
            }
        }

        // DELETE: Удаление файла или папки
        [HttpDelete("{*path}")]
        public IActionResult Delete(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                _logger.LogWarning("Попытка удаления без указания пути");
                return BadRequest("Путь обязателен.");
            }

            string fullPath = Path.Combine(_storagePath, path.Replace("/", Path.DirectorySeparatorChar.ToString()));

            try
            {
                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                    _logger.LogInformation("Файл удалён: {FilePath}", fullPath);
                    return Ok("Файл успешно удалён.");
                }
                else if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, true);
                    _logger.LogInformation("Папка удалена: {Directory}", fullPath);
                    return Ok("Папка успешно удалена.");
                }
                else
                {
                    _logger.LogWarning("Файл или папка не найдены: {Path}", fullPath);
                    return NotFound("Файл или папка не найдены.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении: {Path}", fullPath);
                return StatusCode(500, "Ошибка при удалении: " + ex.Message);
            }
        }
    }
}
