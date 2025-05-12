using System;
using System.Collections.Generic;
using System.Linq;

namespace AppBasicaCRUD
{
    class Program
    {
        static void Main(string[] args)
        {
            List<string> nombres = new List<string>();

            while (true)
            {
                Console.WriteLine("Opciones:");
                Console.WriteLine("1. Agregar nombre");
                Console.WriteLine("2. Mostrar nombres");
                Console.WriteLine("3. Eliminar nombre");
                Console.WriteLine("4. Salir");

                Console.Write("Seleccione una opción: ");
                string opcion = Console.ReadLine();

                switch (opcion)
                {
                    case "1":
                        Console.Write("Ingrese el nombre a agregar: ");
                        string nuevoNombre = Console.ReadLine();
                        nombres.Add(nuevoNombre);
                        Console.WriteLine("Nombre agregado.");
                        break;
                    case "2":
                        if (nombres.Count == 0)
                        {
                            Console.WriteLine("No hay nombres para mostrar.");
                        }
                        else
                        {
                            Console.WriteLine("Lista de nombres:");
                            foreach (string nombre in nombres)
                            {
                                Console.WriteLine(nombre);
                            }
                        }
                        break;
                    case "3":
                        if (nombres.Count == 0)
                        {
                            Console.WriteLine("No hay nombres para eliminar.");
                        }
                        else
                        {
                            Console.Write("Ingrese el nombre a eliminar: ");
                            string nombreAEliminar = Console.ReadLine();
                            if (nombres.Contains(nombreAEliminar))
                            {
                                nombres.Remove(nombreAEliminar);
                                Console.WriteLine("Nombre eliminado.");
                            }
                            else
                            {
                                Console.WriteLine("El nombre no se encuentra en la lista.");
                            }
                        }
                        break;
                    case "4":
                        Console.WriteLine("Saliendo...");
                        return;
                    default:
                        Console.WriteLine("Opción inválida.");
                        break;
                }

                Console.WriteLine();
            }
        }
    }
}