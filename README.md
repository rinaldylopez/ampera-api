# 📌 AuditWorkflow API

## 🧾 Descripción

**AuditWorkflow** es una API desarrollada en **ASP.NET Core** cuyo propósito es gestionar la autenticación de usuarios y controlar el acceso a funcionalidades del sistema mediante perfiles de navegación.

El sistema no maneja roles de seguridad tradicionales, sino **perfiles de navegación**, los cuales determinan qué módulos y páginas puede visualizar cada usuario dentro de la aplicación.

---

## 🏗️ Arquitectura del Proyecto

El proyecto está organizado en las siguientes capas:

### 📂 Controllers

Contiene los endpoints de la API que exponen la funcionalidad al cliente.

### 📂 Models

Define las entidades del sistema, como:

* Usuarios
* Empresas
* Perfiles de navegación
* Módulos
* Ítems de navegación

### 📂 Data

Configuración de base de datos y contexto de Entity Framework.

### 📂 Services

Contiene la lógica de negocio, como:

* Autenticación
* Validaciones
* Procesos internos

### 📂 Settings

Configuraciones generales (ej. JWT, conexiones, etc.)

---

## 🔐 Funcionalidades Principales

### 👤 Autenticación

* Validación de credenciales
* Generación de respuesta de usuario
* Preparado para integración con JWT

### 🧭 Navegación Dinámica

* Perfiles de navegación asignados a usuarios
* Control de acceso a páginas (ítems)
* Agrupación de páginas por módulos

### 🏢 Gestión de Empresas

* Asociación de usuarios a empresas

---

## 🧠 Modelo de Navegación

El sistema utiliza una estructura flexible:

* **User**

  * Pertenece a una empresa
  * Tiene un perfil de navegación

* **NavigationProfile**

  * Define qué puede ver el usuario

* **NavigationItem**

  * Representa una página o funcionalidad

* **Module**

  * Agrupa ítems de navegación

* **NavigationProfileItem**

  * Relación entre perfiles e ítems

---

## 🚀 Ejecución del Proyecto

1. Restaurar paquetes:

   ```bash
   dotnet restore
   ```

2. Ejecutar la aplicación:

   ```bash
   dotnet run
   ```

3. Acceder a Swagger:

   ```
   https://localhost:{puerto}/swagger
   ```

---

## ⚙️ Configuración

Editar el archivo:

```
appsettings.json
```

Para definir:

* Cadena de conexión
* Configuración de seguridad
* Otros parámetros del sistema

---

## 🔧 Tecnologías Utilizadas

* .NET 6 / .NET 7+
* ASP.NET Core Web API
* Entity Framework Core
* Swagger (Swashbuckle)

---

## 📌 Notas

* El sistema está diseñado para ser escalable
* Se recomienda implementar:

  * JWT para autenticación
  * Hash de contraseñas (bcrypt)
  * Control de intentos de acceso

---

## 📈 Futuras Mejoras

* Implementación de refresh tokens
* Control de permisos a nivel de endpoint
* Auditoría de acciones de usuario
* Caché de navegación

---

## 👨‍💻 Autor

Proyecto desarrollado como base para sistemas empresariales con control de acceso dinámico y estructura modular.

---
