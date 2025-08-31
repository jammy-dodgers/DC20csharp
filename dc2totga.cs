/*
 *
 *  Converts raw data files of Chinon ES-1000 or Kodak DC20 to TGA files.
 *
 *  based on cmttoppm.c written by YOSHIDA Hideki <hideki@yk.rim.or.jp>
 *  enhanced to dc2totga.c by Oliver.Hartmann@t-online.de
 *
 */


static class dc20pack
{
    /// <summary>
    /// sorry about the naming of stuff around here i kinda just wanted to get it done
    /// </summary>
    private struct Semiglobals
    {
        public byte[][] ccd;
        public long[][] horiz_ipol;
        public long[][] red;
        public long[][] green;
        public long[][] blue;
    }
    const int RES_LINES = 375;
    const int LINES = 243;
    const int COLUMNS = 512;
    const int CAMERA_HEADER_SIZE = 512;

    const int TOP_MARGIN = 1;
    const int BOTTOM_MARGIN = 1;
    const int LEFT_MARGIN = 2;
    const int RIGHT_MARGIN = 10;

    const int HORIZ_IPOL = 3;
    const int SCALE = 64;

    private static void read_dc2_file(Semiglobals _, Stream i_f)
    {

        var fsize = i_f.Length;
        int line = 0; int column = 0;


        if (fsize < 124928L) // magic !
        {
            i_f.Seek(CAMERA_HEADER_SIZE / 2, SeekOrigin.Begin);
            for (line = 0; line < LINES; line++)
            {
                for (column = 0; column < COLUMNS; column += 4)
                {
                    var n = i_f.Read(_.ccd[line], 0, 2);
                    if (n < 2)
                    {
                        throw new Exception("SHORT READ");
                    }
                    _.ccd[line][column + 2] = _.ccd[line][column];
                    _.ccd[line][column + 3] = _.ccd[line][column + 1];
                }
                _.ccd[line][2] = _.ccd[line][6];
                _.ccd[line][3] = _.ccd[line][7];
                _.ccd[line][0] = _.ccd[line][2];
                _.ccd[line][1] = _.ccd[line][3];
                _.ccd[line][COLUMNS - RIGHT_MARGIN + 1] = _.ccd[line][COLUMNS - RIGHT_MARGIN - 3];
                _.ccd[line][COLUMNS - RIGHT_MARGIN + 2] = _.ccd[line][COLUMNS - RIGHT_MARGIN - 2];
                for (column = 4; column < COLUMNS - 3; column += 4)
                {
                    _.ccd[line][column] = (byte)((_.ccd[line][column - 2] + _.ccd[line][column + 2]) / 2);
                    _.ccd[line][column + 1] = (byte)((_.ccd[line][column - 1] + _.ccd[line][column + 3]) / 2);
                }
                for (column = 0; column < COLUMNS; column++)
                {
                    if (_.ccd[line][column] < 2)
                    {
                        _.ccd[line][column] = 2;
                    }
                }
            }
        }
        else //big chunky file!
        {
            if (fsize == 124928L)
            {
                i_f.Seek(CAMERA_HEADER_SIZE, SeekOrigin.Begin);
            }
            else if (fsize == 125056L)
            {
                i_f.Seek(CAMERA_HEADER_SIZE + 128, SeekOrigin.Begin);
            }
            else if (fsize == 130048L)
            {
                i_f.Seek(CAMERA_HEADER_SIZE + 5120, SeekOrigin.Begin);
            }
            else
            {
                throw new Exception("weird file");
            }
            for (line = 0; line < LINES; line++)
            {
                var n = i_f.Read(_.ccd[line], 0, COLUMNS);
                if (n < COLUMNS)
                {
                    throw new Exception("SHORT READ");
                }

                for (column = 0; column < COLUMNS; column++)
                {
                    if (_.ccd[line][column] < 2)
                    {
                        _.ccd[line][column] = 2;
                    }
                }
            }
        }
    }

    private static void set_intial_interp(Semiglobals _)
    {
        int column, line;
        for (line = 0; line < LINES; line++)
        {
            _.horiz_ipol[line][LEFT_MARGIN] = _.ccd[line][LEFT_MARGIN + 1] * SCALE;
            _.horiz_ipol[line][COLUMNS - RIGHT_MARGIN - 1] = _.ccd[line][COLUMNS - RIGHT_MARGIN - 2] * SCALE;
            for (column = LEFT_MARGIN + 1; column < COLUMNS - RIGHT_MARGIN - 1; column++)
            {
                _.horiz_ipol[line][column] = (_.ccd[line][column - 1] + _.ccd[line][column + 1]) * (SCALE / 2);
            }
        }
    }

    private static void ipol_horizontally(Semiglobals _)
    {

        int column, line, i, init_col;
        for (line = TOP_MARGIN - 1; line < LINES - BOTTOM_MARGIN + 1; line++)
        {
            for (i = 0; i < HORIZ_IPOL; i++)
            {
                for (init_col = LEFT_MARGIN + 1; init_col <= LEFT_MARGIN + 2; init_col++)
                {
                    for (column = init_col; column < COLUMNS - RIGHT_MARGIN - 1; column += 2)
                    {
                        _.horiz_ipol[line][column] = (int)(((double)_.ccd[line][column - 1] / _.horiz_ipol[line][column - 1] +
                                                            (double)_.ccd[line][column + 1] / _.horiz_ipol[line][column + 1]) *
                                                               _.ccd[line][column] * (SCALE * SCALE / 2) +
                                                           0.5);
                    }
                }
            }
        }
    }

    static void ipol_vertically(Semiglobals _)
    {
        int column, line;
        for (line = TOP_MARGIN; line < LINES - BOTTOM_MARGIN; line++)
        {
            for (column = LEFT_MARGIN; column < COLUMNS - RIGHT_MARGIN; column++)
            {
                long r2gb, g2b, rg2, rgb2, r = 0, g = 0, b = 0;
                long this_ccd = _.ccd[line][column] * SCALE;
                long up_ccd = _.ccd[line - 1][column] * SCALE;
                long down_ccd = _.ccd[line + 1][column] * SCALE;
                long this_horiz_ipol = _.horiz_ipol[line][column];
                long this_intensity = this_ccd + this_horiz_ipol;
                long up_intensity = _.horiz_ipol[line - 1][column] + up_ccd;
                long down_intensity = _.horiz_ipol[line + 1][column] + down_ccd;
                long this_vert_ipol;
                if (line == TOP_MARGIN)
                {
                    this_vert_ipol = (long)((double)down_ccd / down_intensity * this_intensity + 0.5f);
                }
                else if (line == LINES - BOTTOM_MARGIN - 1)
                {
                    this_vert_ipol = (long)((double)up_ccd / up_intensity * this_intensity + 0.5f);
                }
                else
                {
                    this_vert_ipol = (long)(((double)up_ccd / up_intensity + (double)down_ccd / down_intensity) *
                                                this_intensity / 2.0f +
                                            0.5f);
                }
                if ((line & 1) != 0)
                {
                    if ((column & 1) != 0)
                    {
                        r2gb = this_ccd;
                        g2b = this_horiz_ipol;
                        rg2 = this_vert_ipol;
                        r = (2 * (r2gb - g2b) + rg2) / 5;
                        g = (rg2 - r) / 2;
                        b = g2b - 2 * g;
                    }
                    else
                    {
                        g2b = this_ccd;
                        r2gb = this_horiz_ipol;
                        rgb2 = this_vert_ipol;
                        r = (3 * r2gb - g2b - rgb2) / 5;
                        g = 2 * r - r2gb + g2b;
                        b = g2b - 2 * g;
                    }
                }
                else
                {
                    if ((column & 1) != 0)
                    {
                        rg2 = this_ccd;
                        rgb2 = this_horiz_ipol;
                        r2gb = this_vert_ipol;
                        b = (3 * rgb2 - r2gb - rg2) / 5;
                        g = (rgb2 - r2gb + rg2 - b) / 2;
                        r = rg2 - 2 * g;
                    }
                    else
                    {
                        rgb2 = this_ccd;
                        rg2 = this_horiz_ipol;
                        g2b = this_vert_ipol;
                        b = (g2b - 2 * (rg2 - rgb2)) / 5;
                        g = (g2b - b) / 2;
                        r = rg2 - 2 * g;
                    }
                }
                if (r < 0)
                    r = 0;
                if (g < 0)
                    g = 0;
                if (b < 0)
                    b = 0;
                _.red[line][column] = r;
                _.green[line][column] = g;
                _.blue[line][column] = b;
            }
        }
    }


    const double RFACTOR = 0.64;
    const double GFACTOR = 0.58;
    const double BFACTOR = 1.00;
    const double RINTENSITY = 0.476;
    const double GINTENSITY = 0.299;
    const double BINTENSITY = 0.175;

    const double SATURATION = 1.2;
    const double NORM_PERCENTAGE = 0.5;
    const double GAMMA = 0.5;

    private static void adjust_color_and_saturation(Semiglobals _, double saturation = SATURATION, double rfactor = RFACTOR, double gfactor = GFACTOR, double bfactor = BFACTOR)
    {
        int line, column;
        double sqr_saturation = Math.Sqrt(saturation);
        for (line = TOP_MARGIN; line < LINES - BOTTOM_MARGIN; line++)
        {
            for (column = LEFT_MARGIN; column < COLUMNS - RIGHT_MARGIN; column++)
            {
                double r = _.red[line][column] * rfactor;
                double g = _.green[line][column] * gfactor;
                double b = _.blue[line][column] * bfactor;
                if (saturation != 1.0f)
                {
                    unsafe //todo: rewrite
                    {
                        double* min; double* mid; double* max;
                        double new_intensity;
                        double intensity = r * RINTENSITY + g * GINTENSITY + b * BINTENSITY;
                        if (r > g)
                        {
                            if (r > b)
                            {
                                max = &r;
                                if (g > b)
                                {
                                    min = &b;
                                    mid = &g;
                                }
                                else
                                {
                                    min = &g;
                                    mid = &b;
                                }
                            }
                            else
                            {
                                min = &g;
                                mid = &r;
                                max = &b;
                            }
                        }
                        else
                        {
                            if (g > b)
                            {
                                max = &g;
                                if (r > b)
                                {
                                    min = &b;
                                    mid = &r;
                                }
                                else
                                {
                                    min = &r;
                                    mid = &b;
                                }
                            }
                            else
                            {
                                min = &r;
                                mid = &g;
                                max = &b;
                            }
                        }
                        *mid = *min + sqr_saturation * (*mid - *min);
                        *max = *min + saturation * (*max - *min);
                        new_intensity = r * RINTENSITY + g * GINTENSITY + b * BINTENSITY;
                        r *= intensity / new_intensity;
                        g *= intensity / new_intensity;
                        b *= intensity / new_intensity;
                    }
                }
                _.red[line][column] = (long)(r + 0.5);
                _.green[line][column] = (long)(g + 0.5);
                _.blue[line][column] = (long)(b + 0.5);
            }
        }
    }

    private static long lumi(long r, long g, long b)
    {
        return ((3 * r) / 10 + (6 * g) / 10 + b / 10);
    }
    const int HISTOGRAM_STEPS = 4096;
    const int NET_COLUMNS = (COLUMNS - LEFT_MARGIN - RIGHT_MARGIN);
    const int NET_LINES = (LINES - TOP_MARGIN - BOTTOM_MARGIN);
    const int NET_PIXELS = (NET_COLUMNS * NET_LINES);
    const int SMAX = (256 * SCALE - 1);
    private static (long, long) determine_limits(Semiglobals _, double norm_percentage = NORM_PERCENTAGE)
    {
        var histogram = new uint[HISTOGRAM_STEPS + 1];
        int column, line;
        long nrm_perc = (long)(norm_percentage * 100);
        long i, s;
        long low_i = -1, high_i = -1;
        long max_i = 0;
        for (line = TOP_MARGIN; line < LINES - BOTTOM_MARGIN; line++)
        {
            for (column = LEFT_MARGIN; column < COLUMNS - RIGHT_MARGIN; column++)
            {
                /* i = max3(red[line][column], green[line][column], blue[line][column]); */
                i = lumi(_.red[line][column], _.green[line][column], _.blue[line][column]);
                if (i > max_i)
                    max_i = i;
            }
        }
        if (low_i == -1)
        {
            for (i = 0; i <= HISTOGRAM_STEPS; i++)
                histogram[i] = 0;
            for (line = TOP_MARGIN; line < LINES - BOTTOM_MARGIN; line++)
            {
                for (column = LEFT_MARGIN; column < COLUMNS - RIGHT_MARGIN; column++)
                {
                    /* i = min3(red[line][column], green[line][column], blue[line][column]); */
                    i = lumi(_.red[line][column], _.green[line][column], _.blue[line][column]);
                    histogram[i * HISTOGRAM_STEPS / max_i]++;
                }
            }
            s = 0;
            low_i = 0;
            for (; low_i <= HISTOGRAM_STEPS && s < NET_PIXELS * nrm_perc / 10000; low_i++)
            {
                s += histogram[low_i];
            }
            low_i = (low_i * max_i + HISTOGRAM_STEPS / 2) / HISTOGRAM_STEPS;
        }
        if (high_i == -1)
        {
            for (i = 0; i <= HISTOGRAM_STEPS; i++)
                histogram[i] = 0;
            for (line = TOP_MARGIN; line < LINES - BOTTOM_MARGIN; line++)
            {
                for (column = LEFT_MARGIN; column < COLUMNS - RIGHT_MARGIN; column++)
                {
                    /* i = max3(red[line][column], green[line][column], blue [line][column]); */
                    i = lumi(_.red[line][column], _.green[line][column], _.blue[line][column]);
                    histogram[i * HISTOGRAM_STEPS / max_i]++;
                }
            }
            s = 0;
            high_i = HISTOGRAM_STEPS;
            for (; high_i >= 0 && s < NET_PIXELS * nrm_perc / 10000; high_i--)
            {
                s += histogram[high_i];
            }
            high_i = (high_i * max_i + HISTOGRAM_STEPS / 2) / HISTOGRAM_STEPS;
        }
        return (low_i, high_i);
    }

    private static byte[] make_gamma_table(int range)
    {
        int i;
        double gamma_value = GAMMA;
        double factor = Math.Pow(256.0, 1.0 / gamma_value) / range;
        var gamma_table = new byte[range];
        for (i = 0; i < range; i++)
        {
            int g = (int)(Math.Pow((double)i * factor, gamma_value) + 0.5);
            if (g > 255)
                g = 255;
            gamma_table[i] = (byte)g;
        }
        return gamma_table;

    }
    private static int lookup_gamma_table(long i, long low_i, long high_i,
                        byte[] gamma_table)
    {
        if (i <= (int)low_i)
            return 0;
        if (i >= (int)high_i)
            return 255;
        return gamma_table[i - low_i];
    }


    private static void stretch(Semiglobals _, long low_i, long high_i)
    {
        int column, line, i;
        byte max_ccd_val = 255;
        byte[] gamma_table = make_gamma_table((int)(high_i - low_i));

        for (line = TOP_MARGIN; line < LINES - BOTTOM_MARGIN; line++)
        {
            for (column = LEFT_MARGIN; column < COLUMNS - RIGHT_MARGIN; column++)
            {
                int r = lookup_gamma_table(_.red[line][column], low_i, high_i, gamma_table);
                int g = lookup_gamma_table(_.green[line][column], low_i, high_i, gamma_table);
                int b = lookup_gamma_table(_.blue[line][column], low_i, high_i, gamma_table);

                if (_.ccd[line][column] >= max_ccd_val)
                    if (_.ccd[line][column - 1] >= max_ccd_val ||
                        _.ccd[line][column + 1] >= max_ccd_val ||
                        _.ccd[line - 1][column] >= max_ccd_val ||
                        _.ccd[line + 1][column] >= max_ccd_val)
                    {
                        r = g = b = _.ccd[line][column];
                    }

                if (r > 255)
                    r = 255;
                else if (r < 0)
                    r = 0;
                if (g > 255)
                    g = 255;
                else if (g < 0)
                    g = 0;
                if (b > 255)
                    b = 255;
                else if (b < 0)
                    b = 0;
                _.red[line - TOP_MARGIN][column] = (short)r;
                _.green[line - TOP_MARGIN][column] = (short)g;
                _.blue[line - TOP_MARGIN][column] = (short)b;
            }
        }


        for (line = RES_LINES - 1; line >= 0; line--)
        {
            float fy = NET_LINES / (float)RES_LINES;
            float by, ey;
            int byab, eyab;
            float teilzeile;
            uint ufakt;
            uint ofakt;

            by = fy * line;
            ey = fy * (line + 1);

            byab = (int)by;
            eyab = (int)ey;
            if ((float)eyab == ey)
                eyab--;

            teilzeile = eyab / fy;
            ufakt = (uint)((teilzeile - line) * 256);
            ofakt = (uint)((line + 1 - teilzeile) * 256);

            for (column = LEFT_MARGIN; column < COLUMNS - RIGHT_MARGIN; column++)
            {
                uint erg_blau = 128;
                uint erg_gruen = 128;
                uint erg_rot = 128;

                if (byab != eyab)
                {
                    for (i = eyab; i >= byab; i--)
                    {
                        uint sum_blau = (uint)_.blue[i][column];
                        uint sum_gruen = (uint)_.green[i][column];
                        uint sum_rot = (uint)_.red[i][column];

                        if (i == byab)
                        {
                            sum_blau *= ufakt;
                            sum_gruen *= ufakt;
                            sum_rot *= ufakt;
                        }
                        else
                        {
                            sum_blau *= ofakt;
                            sum_gruen *= ofakt;
                            sum_rot *= ofakt;
                        }
                        erg_blau += sum_blau;
                        erg_gruen += sum_gruen;
                        erg_rot += sum_rot;
                    }
                    _.blue[line][column] = (short)(erg_blau / 256);
                    _.green[line][column] = (short)(erg_gruen / 256);
                    _.red[line][column] = (short)(erg_rot / 256);
                }
                else
                {
                    _.blue[line][column] = _.blue[byab][column];
                    _.green[line][column] = _.green[byab][column];
                    _.red[line][column] = _.red[byab][column];
                }
            } /* Ende Spalte Zielbild */
        } /* Ende Zeile  Zielbild */

    }
    private static void sharpen(Semiglobals _)
    {
        long r, g, b, f11 = 0;
        long[][] f = new[] {
           new[] {0l,0,0},
           new[] {0l,0,0},
           new[] {0l,0,0}
        };
        long j, l, fakt;
        long typ = 1, percent;
        long opt_lev = 2;
        percent = opt_lev * 3;

        if (percent <= 0)
            percent = 1;
        else if (percent > 50)
            percent = 50;

        fakt = 100 / percent - 1;

        switch (typ)
        {
            case 0:
                f11 = fakt + 4;
                f[0][0] = 0;
                f[0][1] = -1;
                f[0][2] = 0;
                f[1][0] = -1;
                f[1][1] = f11;
                f[1][2] = -1;
                f[2][0] = 0;
                f[2][1] = -1;
                f[2][2] = 0;
                break;

            case 1:
                f11 = fakt + 8;
                f[0][0] = -1;
                f[0][1] = -1;
                f[0][2] = -1;
                f[1][0] = -1;
                f[1][1] = f11;
                f[1][2] = -1;
                f[2][0] = -1;
                f[2][1] = -1;
                f[2][2] = -1;
                break;

            default:
                break;
        }

        for (l = 1; l < (RES_LINES - 1); l++)
        {
            for (j = LEFT_MARGIN + 1; j < (COLUMNS - RIGHT_MARGIN - 1); j++)
            {
                r = _.red[l][j] * f11 - _.red[l - 1][j - 1] - _.red[l - 1][j] - _.red[l - 1][j + 1] -
                    _.red[l][j - 1] - _.red[l][j + 1] -
                    _.red[l + 1][j - 1] - _.red[l + 1][j] - _.red[l + 1][j + 1];
                g = _.green[l][j] * f11 - _.green[l - 1][j - 1] - _.green[l - 1][j] - _.green[l - 1][j + 1] -
                    _.green[l][j - 1] - _.green[l][j + 1] -
                    _.green[l + 1][j - 1] - _.green[l + 1][j] - _.green[l + 1][j + 1];
                b = _.blue[l][j] * f11 - _.blue[l - 1][j - 1] - _.blue[l - 1][j] - _.blue[l - 1][j + 1] -
                    _.blue[l][j - 1] - _.blue[l][j + 1] -
                    _.blue[l + 1][j - 1] - _.blue[l + 1][j] - _.blue[l + 1][j + 1];
                if (fakt > 1)
                {
                    r /= fakt;
                    g /= fakt;
                    b /= fakt;
                }
                if (r > 255)
                    r = 255;
                else if (r < 0)
                    r = 0;
                if (g > 255)
                    g = 255;
                else if (g < 0)
                    g = 0;
                if (b > 255)
                    b = 255;
                else if (b < 0)
                    b = 0;

                _.red[l][j] = (short)r;
                _.green[l][j] = (short)g;
                _.blue[l][j] = (short)b;
            }
        }
    }
    private static byte[] output_tga(Semiglobals _)
    {
        int column, line;

        using var o_f = new MemoryStream();

        //tga header
        o_f.Write([0,
                    0,
                    2,
                    0, 0, 0, 0, 0,
                    0, 0, 0, 0]);
        o_f.Write(BitConverter.GetBytes((ushort)(NET_COLUMNS - 4)));
        o_f.Write(BitConverter.GetBytes((ushort)RES_LINES));
        o_f.Write([0x18, 0x20]);

        for (line = 0; line < RES_LINES; line++)
        {
            for (column = LEFT_MARGIN + 2; column < COLUMNS - RIGHT_MARGIN - 2; column++)
            {
                long r = _.red[line][column];
                long g = _.green[line][column];
                long b = _.blue[line][column];
                o_f.WriteByte((byte)b);
                o_f.WriteByte((byte)g);
                o_f.WriteByte((byte)r);
            }
        }
        return o_f.ToArray();
    }

    public static byte[] Dc2totga(byte[] raw)
    {
        //not handling those stupid thumbnail things
        Semiglobals semiglobals = new();
        using var i_f = new MemoryStream(raw);
        semiglobals.ccd = new byte[RES_LINES][];
        semiglobals.horiz_ipol = new long[RES_LINES][];
        semiglobals.red = new long[RES_LINES][];
        semiglobals.green = new long[RES_LINES][];
        semiglobals.blue = new long[RES_LINES][];
        for (int i = 0; i < RES_LINES; i++)
        {
            semiglobals.ccd[i] = new byte[COLUMNS];
            semiglobals.horiz_ipol[i] = new long[COLUMNS];
            semiglobals.red[i] = new long[COLUMNS];
            semiglobals.green[i] = new long[COLUMNS];
            semiglobals.blue[i] = new long[COLUMNS];
        }

        read_dc2_file(semiglobals, i_f);
        set_intial_interp(semiglobals);
        ipol_horizontally(semiglobals);
        ipol_vertically(semiglobals);
        adjust_color_and_saturation(semiglobals);
        var (low_i, high_i) = determine_limits(semiglobals);
        stretch(semiglobals, low_i, high_i);
        // sharpen(semiglobals);
        return output_tga(semiglobals);
    }
}