﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using SearchImagesService.Common.Db;

namespace SearchImagesService.Migrations
{
    [DbContext(typeof(SearchImageContext))]
    [Migration("20190329043734_bl-tags-db")]
    partial class bltagsdb
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("searchimages")
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn)
                .HasAnnotation("ProductVersion", "2.2.3-servicing-35854")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            modelBuilder.Entity("SearchImagesService.Common.Db.Models.BlacklistedTags", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id");

                    b.Property<DateTime>("DateAdded")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("dateadded")
                        .HasDefaultValueSql("timezone('utc', now())");

                    b.Property<decimal>("GuildId")
                        .HasConversion(new ValueConverter<decimal, decimal>(v => default(decimal), v => default(decimal), new ConverterMappingHints(precision: 20, scale: 0)))
                        .HasColumnName("guildid");

                    b.Property<string[]>("Tags")
                        .HasColumnName("tags");

                    b.HasKey("Id")
                        .HasName("pk_blacklistedtags");

                    b.ToTable("blacklistedtags");
                });
#pragma warning restore 612, 618
        }
    }
}
